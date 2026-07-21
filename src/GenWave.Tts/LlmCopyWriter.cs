namespace GenWave.Tts;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// LLM-backed <see cref="ISegmentCopyWriter"/> (SPEC F34.2-F34.5): authors <see cref="SegmentKind.LeadIn"/>
/// and <see cref="SegmentKind.BackAnnounce"/> copy from an OpenAI-compatible chat-completions endpoint.
/// <see cref="SegmentKind.StationId"/> and <see cref="SegmentKind.TimeDate"/> always delegate straight
/// to <paramref name="fallback"/> with zero HTTP — brand/time copy stays fixed and forever-cached.
/// Enabled-ness and every other option are read from <paramref name="optionsMonitor"/> fresh on each
/// call (F36.2) — an empty <c>Llm:Endpoint</c> means disabled. Any failure (disabled, timeout,
/// non-2xx, connect, empty/over-length copy) degrades to <paramref name="fallback"/>'s template copy
/// with exactly one WARN; this writer never throws toward
/// <see cref="GenWave.Core.Abstractions.ITtsSegmentSource"/> (F12.4 extended).
///
/// <paramref name="personaAccessor"/> is resolved once per LeadIn/BackAnnounce render (SPEC F35.2,
/// F35.3) — never for the templated kinds or a disabled writer — and, when it yields a persona,
/// composes an appended backstory + style section onto the baked system prompt (see
/// <see cref="BuildPersonaSection"/>). No persona active, or one with empty Backstory/Style, leaves
/// the prompt exactly as it was before T6 (F35.2 — blurbs work persona-less).
///
/// <para>
/// Every backend completion call — on-air (<see cref="WriteAsync"/>) and preview
/// (<see cref="WritePreviewAsync"/>) alike — is serialized single-flight through
/// <see cref="RequestCompletionAsync"/> (SPEC F69.6, gh-#36): two concurrent renders on the same
/// backend double each other's latency, so this writer (a DI singleton, see
/// <c>TtsServiceCollectionExtensions</c>) holds the one gate both seams share.
/// </para>
/// </summary>
public sealed class LlmCopyWriter(
    TemplateCopyWriter fallback,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<LlmOptions> optionsMonitor,
    LlmCopyStatusHolder statusHolder,
    IActivePersonaAccessor personaAccessor,
    ILogger<LlmCopyWriter> logger) : ISegmentCopyWriter, IPersonaPreviewWriter
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client this writer resolves (registered in Program.cs).</summary>
    public const string HttpClientName = "Llm";

    /// <summary>
    /// Response-buffer ceiling for <see cref="HttpClientName"/> (T3 review finding): a completions
    /// reply is a few sentences of copy, never megabytes — a misbehaving/compromised endpoint
    /// shouldn't be able to make this writer buffer an unbounded response body. Applied to the
    /// named <see cref="HttpClient"/> in Program.cs via <c>HttpClient.MaxResponseContentBufferSize</c>.
    /// 1 MiB is generous headroom over any real completion payload.
    /// </summary>
    public const long MaxResponseContentBytes = 1_048_576;

    /// <summary>
    /// Serializes every backend completion call (SPEC F69.6, gh-#36) — concurrent CPU generations on
    /// the same backend double each other's latency, so at most one <see cref="RequestCompletionAsync"/>
    /// runs at a time, whether it arrived via the on-air path or a persona preview. A queueing wait
    /// (<c>WaitAsync(ct)</c>), not a skip-if-busy latch (contrast
    /// <c>GenWave.MediaLibrary.Scan.ScanService</c>'s own single-flight semaphore): a caller waits its
    /// turn rather than being dropped, and a caller whose own token cancels while still queued throws
    /// straight out of <c>WaitAsync</c> without ever acquiring the gate, so it can never hold up the
    /// next caller in line.
    /// </summary>
    readonly SemaphoreSlim singleFlight = new(1, 1);

    // T6 reviewer follow-up (T4): Backstory/Style are unbounded operator-entered text (F35.1 has no
    // length cap on the persona row) that flows straight into an LLM prompt. Capped per-field rather
    // than left open so one oversized persona can't balloon every render's request body / token
    // spend; a few thousand chars is generous for backstory/style prose while still bounding it.
    const int MaxPersonaSectionFieldChars = 4000;

    static readonly Regex NewlinePattern = new(@"\r\n|\r|\n", RegexOptions.Compiled);
    static readonly Regex BracketStageDirectionPattern = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    // A single word wrapped in one asterisk on each side (no internal spaces) reads as a stage
    // direction — *chuckles*, *laughs* — and is dropped whole. A multi-word wrap (the common
    // markdown emphasis shape, "*Next up*"/"**Next up**") survives this pass and loses only its
    // delimiters below.
    static readonly Regex AsteriskStageDirectionPattern = new(@"\*[^\s*]+\*", RegexOptions.Compiled);
    static readonly Regex MarkdownEmphasisPattern = new(@"[*_]+", RegexOptions.Compiled);
    static readonly Regex RepeatedWhitespacePattern = new(@"\s{2,}", RegexOptions.Compiled);

    public async Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct)
    {
        // StationId/TimeDate stay templated — brand/time copy must be crisp, consistent, and
        // forever-cacheable; only the two track-anchored kinds have tags worth an LLM's while (F34.2).
        if (request.Kind is not (SegmentKind.LeadIn or SegmentKind.BackAnnounce))
            return await fallback.WriteAsync(request, ct);

        var attemptedAt = DateTimeOffset.UtcNow;
        // Hoisted above the try (SPEC F69.7 review finding) so the catch-all below can still cite
        // them as call context even when the fault is EARLIER than the line that would have set
        // them — e.g. an OptionsValidationException thrown from the CurrentValue getter itself,
        // before cfg is ever assigned.
        LlmOptions? cfg = null;
        Persona? persona = null;
        try
        {
            // CurrentValue is read INSIDE the try (T3 review finding): a live edit that leaves
            // Llm:* failing its own validators raises OptionsValidationException from this very
            // property getter, and that must land on the catch-all below like any other miss
            // (F12.4), not escape past the fallback ladder toward the caller.
            cfg = optionsMonitor.CurrentValue;
            if (string.IsNullOrEmpty(cfg.Endpoint))
                return await fallback.WriteAsync(request, ct);

            // Resolved ONLY on this LLM path (F35.2) — templated kinds and the disabled writer
            // never call the accessor at all, so a persona plays no part in copy they never touch.
            // Re-read fresh per render, never cached (F35.5): a live activate/deactivate takes
            // effect on the very next segment. The accessor's own contract never throws, but this
            // call already sits inside the catch-all below, so an unexpected fault still degrades
            // to the template rung like every other miss (F12.4).
            persona = await personaAccessor.ResolveAsync(ct);
            var raw = await RequestCompletionAsync(cfg, request, persona, ct);
            var cleaned = CleanCopy(raw, cfg.MaxCopyChars);
            if (cleaned is null)
            {
                statusHolder.Record(LlmAttemptOutcome.Failed, attemptedAt);
                LogFailure(request, persona, cfg.Model, attemptedAt, exception: null,
                    reason: "empty or exceeded Llm:MaxCopyChars after cleanup");
                return await fallback.WriteAsync(request, ct);
            }

            statusHolder.Record(LlmAttemptOutcome.Ok, attemptedAt);
            // Only genuinely LLM-authored copy is fresh-per-airing (F34.6) — every fallback path
            // above returns the template writer's own SegmentCopy (FreshPerAiring: false) unchanged.
            return new SegmentCopy(cleaned, FreshPerAiring: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled (e.g. shutdown) — not our own Llm:TimeoutSeconds budget expiring.
            // Propagate; this is not an LLM failure to record or warn about.
            throw;
        }
        catch (Exception ex)
        {
            // Everything else lands here: our own timeout CTS firing, a non-2xx status
            // (EnsureSuccessStatusCode), a connect failure, a malformed endpoint URI, bad JSON.
            // Every one of these degrades to the template rung with exactly one WARN (F34.4),
            // carrying the exception type/status plus call context (F69.7).
            statusHolder.Record(LlmAttemptOutcome.Failed, attemptedAt);
            LogFailure(request, persona, cfg?.Model, attemptedAt, ex, reason: null);
            return await fallback.WriteAsync(request, ct);
        }
    }

    /// <summary>
    /// <see cref="IPersonaPreviewWriter"/> (SPEC F35.6, STORY-123) — reuses
    /// <see cref="RequestCompletionAsync"/> (identical prompt composition) and
    /// <see cref="CleanCopy"/> (identical hygiene) so the previewed text is provably what the
    /// on-air <see cref="WriteAsync"/> path would have produced for the same request/persona. The
    /// one deliberate difference: NOTHING here degrades to <paramref name="fallback"/> on an LLM
    /// miss for LeadIn/BackAnnounce — that would misrepresent the persona being auditioned — and
    /// this method never records to <see cref="LlmCopyStatusHolder"/> (that holder tracks on-air
    /// attempts for <c>GET /api/status</c>; preview activity never airs and must not appear there).
    /// </summary>
    public async Task<PersonaPreviewResult> WritePreviewAsync(
        SegmentRequest request, Persona? personaOverride, CancellationToken ct)
    {
        // StationId/TimeDate route straight to the template rung — mirrors WriteAsync's own
        // kind-based routing (F34.2). This is not a fallback: those two kinds never call the LLM
        // on-air either, so template text IS the correct preview for them.
        if (request.Kind is not (SegmentKind.LeadIn or SegmentKind.BackAnnounce))
        {
            var templated = await fallback.WriteAsync(request, ct);
            return new PersonaPreviewResult.Success(templated.Text);
        }

        var cfg = optionsMonitor.CurrentValue;
        if (string.IsNullOrEmpty(cfg.Endpoint))
            return new PersonaPreviewResult.Failed("The LLM endpoint is not configured.");

        var attemptedAt = DateTimeOffset.UtcNow;
        try
        {
            var raw = await RequestCompletionAsync(cfg, request, personaOverride, ct);
            var cleaned = CleanCopy(raw, cfg.MaxCopyChars);
            return cleaned is null
                ? new PersonaPreviewResult.Failed("The LLM returned empty or over-length copy.")
                : new PersonaPreviewResult.Success(cleaned);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled — not our own Llm:TimeoutSeconds budget expiring. Propagate;
            // this is not an LLM failure to report as a preview result.
            throw;
        }
        catch (Exception ex)
        {
            // Same failure surface WriteAsync degrades from (our own timeout CTS, a non-2xx
            // status, a connect failure, a malformed endpoint URI, bad JSON) — the preview
            // reports it honestly instead of substituting template text (F35.6), and the WARN
            // carries the exception type/status plus call context exactly like WriteAsync's own
            // catch-all (F69.7).
            LogFailure(request, personaOverride, cfg.Model, attemptedAt, ex, reason: null, previewOnly: true);
            return new PersonaPreviewResult.Failed("The LLM request failed. Check the server logs for details.");
        }
    }

    /// <summary>
    /// One consistent WARN for every failure this writer produces (SPEC F69.7 — closes the
    /// detail-free warn gap): states either the exception type (or, for a non-2xx response, the
    /// HTTP status the runtime already captured on <see cref="HttpRequestException.StatusCode"/>)
    /// or, for a same-call content reject that never threw, <paramref name="reason"/> — plus enough
    /// call context (segment kind, persona identity if one was in scope, station, model, elapsed
    /// ms) to diagnose the miss from this one line. Deliberately excludes the prompt itself:
    /// backstory/style/user copy is operator content that belongs in the ring inspector (T41), never
    /// at WARN.
    /// </summary>
    void LogFailure(
        SegmentRequest request, Persona? persona, string? model, DateTimeOffset attemptedAt,
        Exception? exception, string? reason, bool previewOnly = false)
    {
        var detail = exception switch
        {
            HttpRequestException { StatusCode: { } status } => $"HTTP {(int)status}",
            { } ex => ex.GetType().Name,
            null => reason ?? "unknown failure",
        };
        var elapsedMs = (long)(DateTimeOffset.UtcNow - attemptedAt).TotalMilliseconds;
        var outcome = previewOnly ? "reporting failure to the preview caller" : "falling back to template";

        // Operator-authored values (persona name, model, exception-derived detail) are
        // newline-stripped so they can't forge additional log entries (CodeQL cs/log-forging).
        logger.LogWarning(
            exception,
            "LLM completion failed for {Kind} on station {StationId} (persona: {PersonaName}, " +
            "model: {Model}, elapsed: {ElapsedMs}ms): {Detail} — {Outcome}",
            request.Kind, request.StationId,
            (persona?.Name ?? "none").ReplaceLineEndings(" "),
            (model ?? "unknown").ReplaceLineEndings(" "),
            elapsedMs,
            detail.ReplaceLineEndings(" "), outcome);
    }

    async Task<string> RequestCompletionAsync(
        LlmOptions cfg, SegmentRequest request, Persona? persona, CancellationToken ct)
    {
        // Single-flight (SPEC F69.6, gh-#36): acquired BEFORE the per-call timeout clock starts, so
        // a caller queued behind another generation is waiting its turn, not burning its own
        // Llm:TimeoutSeconds budget — and a caller whose own ct cancels while still queued (e.g.
        // shutdown) throws right out of WaitAsync without ever holding the gate.
        await singleFlight.WaitAsync(ct);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

            var http = httpClientFactory.CreateClient(HttpClientName);

            // No boot-frozen BaseAddress (F36.2) — the endpoint is read from CurrentValue above and an
            // absolute URI is built per call (EndpointUri preserves a subpath in Llm:Endpoint, e.g.
            // https://host/openai — a plain new Uri(base, "/v1/...") would drop it, T3 review finding),
            // so a live PUT to Llm:Endpoint applies on the next render.
            var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");

            var body = new
            {
                model = cfg.Model,
                messages = new object[]
                {
                    new { role = "system", content = BuildSystemPrompt(BuildPersonaSection(persona)) },
                    new { role = "user", content = BuildUserContent(request) },
                },
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(body),
            };

            // Bearer header rides only when an ApiKey is configured (env-only, F19.3/F34.3).
            if (!string.IsNullOrEmpty(cfg.ApiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            }

            var response = await http.SendAsync(httpRequest, timeoutCts.Token);
            response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

            var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(timeoutCts.Token);
            return payload?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
        finally
        {
            singleFlight.Release();
        }
    }

    /// <summary>
    /// Baked house scaffold for the system prompt (SPEC F34.3): personality-neutral radio DJ, 1-2
    /// spoken sentences, no stage directions. <paramref name="personaSection"/> (SPEC F35.2, F35.3)
    /// appends an active persona's backstory + style beneath the scaffold; null/empty (no active
    /// persona, or one with no non-empty Backstory/Style) leaves the neutral scaffold untouched —
    /// blurbs work persona-less exactly as before T6.
    /// </summary>
    static string BuildSystemPrompt(string? personaSection)
    {
        const string Scaffold =
            "You are a personality-neutral radio DJ writing live station patter. Write exactly one " +
            "or two sentences of spoken copy to be read aloud on air. Plain spoken words only - no " +
            "stage directions, no emoji, no markdown formatting, no sound-effect cues. You may " +
            "embellish with genuine knowledge of the track, artist, or era.";

        return string.IsNullOrEmpty(personaSection) ? Scaffold : $"{Scaffold}\n\n{personaSection}";
    }

    /// <summary>
    /// Composes the persona section from <see cref="Persona.Backstory"/> + <see cref="Persona.Style"/>
    /// (SPEC F35.2, F35.3): each present field becomes one labeled line, empty fields are skipped
    /// entirely, and a persona with neither yields null (falls back to the neutral scaffold — the
    /// "neutral otherwise" half of F35.2, not just the no-persona case). Each field is capped at
    /// <see cref="MaxPersonaSectionFieldChars"/> before it reaches the prompt.
    /// </summary>
    static string? BuildPersonaSection(Persona? persona)
    {
        if (persona is null)
            return null;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(persona.Backstory))
            lines.Add($"Backstory: {Truncate(persona.Backstory, MaxPersonaSectionFieldChars)}");
        if (!string.IsNullOrEmpty(persona.Style))
            lines.Add($"Style: {Truncate(persona.Style, MaxPersonaSectionFieldChars)}");

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars];

    static string BuildUserContent(SegmentRequest request)
    {
        var lines = new List<string>
        {
            $"Station: {request.StationName}",
            $"Local time: {request.LocalNow:yyyy-MM-dd HH:mm}",
            request.Kind == SegmentKind.LeadIn
                ? "Segment: lead-in for the upcoming track."
                : "Segment: back-announce for the track that just played.",
        };

        if (request.Track is { } track)
        {
            lines.Add($"Title: {track.Title}");
            if (!string.IsNullOrEmpty(track.Artist)) lines.Add($"Artist: {track.Artist}");
            if (!string.IsNullOrEmpty(track.Album)) lines.Add($"Album: {track.Album}");
            if (!string.IsNullOrEmpty(track.Genre)) lines.Add($"Genre: {track.Genre}");
            if (track.Year is { } year) lines.Add($"Year: {year}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Copy hygiene (SPEC F34.5): trims, unwraps one layer of wrapping quotes, collapses newlines to
    /// spaces, and strips stage directions and markdown emphasis markers. Returns null when the
    /// result is empty or still exceeds <paramref name="maxChars"/> after cleanup — the caller
    /// rejects to the fallback rather than truncate mid-sentence.
    /// </summary>
    static string? CleanCopy(string raw, int maxChars)
    {
        var text = StripWrappingQuotes(raw.Trim());
        text = NewlinePattern.Replace(text, " ");
        text = BracketStageDirectionPattern.Replace(text, string.Empty);
        text = AsteriskStageDirectionPattern.Replace(text, string.Empty);
        text = MarkdownEmphasisPattern.Replace(text, string.Empty);
        text = RepeatedWhitespacePattern.Replace(text, " ").Trim();

        if (text.Length == 0)
            return null;

        return text.Length > maxChars ? null : text;
    }

    static string StripWrappingQuotes(string text)
    {
        if (text.Length >= 2)
        {
            var first = text[0];
            var last = text[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                return text[1..^1].Trim();
        }

        return text;
    }
}
