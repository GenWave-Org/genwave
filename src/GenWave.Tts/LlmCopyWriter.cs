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
/// F35.3, F71.3) — never for the templated kinds or a disabled writer — both for the legacy
/// <c>Persona</c> row AND its card counterpart, composing an appended soul + sampled-quirks section
/// onto the baked system prompt (see <see cref="LlmPromptBuilder.BuildPersonaSection"/>). No persona
/// active, and no soul/quirks to show, leaves the prompt exactly as it was before T6 (F35.2 —
/// blurbs work persona-less).
///
/// <para>
/// The DJ's clock (SPEC F71.8, gh-#13, STORY-193): <paramref name="timeProvider"/> stamps every
/// prompt this writer builds — persona active or not — with the current date/weekday/time in
/// station-local terms (see <see cref="LlmPromptBuilder.BuildStationClockLine"/>), so the model
/// answers from the injected clock rather than inventing one.
/// </para>
///
/// <para>
/// Every backend completion call — on-air (<see cref="WriteAsync"/>) and preview
/// (<see cref="WritePreviewAsync"/>) alike — is serialized single-flight through
/// <see cref="RequestCompletionAsync"/> (SPEC F69.6, gh-#36): two concurrent renders on the same
/// backend double each other's latency, so this writer (a DI singleton, see
/// <c>TtsServiceCollectionExtensions</c>) holds the one gate both seams share.
/// </para>
///
/// <para>
/// The SAME single recording point (SPEC F73.1, STORY-196, T41) is also where every call — on-air,
/// Soft-cadence, or preview — lands in <see cref="LlmCallRing"/>, the admin call inspector's
/// in-memory ring: prompt, raw response, timing, outcome, and the degradation mode active at call
/// time (<see cref="IDegradationModeReader.CurrentMode"/>, read fresh right here rather than passed
/// in — a preview never passes through <see cref="DegradationGatedCopyWriter"/>, so there is no
/// caller-supplied mode to reuse for that path; reading it uniformly for every path keeps this the
/// one recording point instead of two). Never logged, never persisted — see <see cref="LlmCallRing"/>'s
/// own remarks.
/// </para>
/// </summary>
public sealed class LlmCopyWriter(
    TemplateCopyWriter fallback,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<LlmOptions> optionsMonitor,
    LlmCopyStatusHolder statusHolder,
    IActivePersonaAccessor personaAccessor,
    ILogger<LlmCopyWriter> logger,
    TimeProvider timeProvider,
    LlmCallRing callRing,
    IDegradationModeReader degradationMode) : ISegmentCopyWriter, IPersonaPreviewWriter
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
            // The card counterpart (SPEC F71.1, F71.3, STORY-193) — same never-throws, re-read-fresh
            // contract as ResolveAsync above. Soul/quirks are sourced from THIS, with legacy
            // Backstory/Style as the fallback (see LlmPromptBuilder.BuildSoul's own remarks).
            var card = await personaAccessor.ResolveCardAsync(ct);
            var raw = await RequestCompletionAsync(cfg, request, persona, card, ct);
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
            // No card here, by design: a preview audits the EXPLICIT personaOverride the caller
            // handed in (an in-progress admin edit, possibly never saved) — there is no "active
            // persona's card" to resolve that would correspond to it. Soul falls back to the
            // legacy Backstory/Style composition (see LlmPromptBuilder.BuildSoul); quirks stay
            // absent. The clock (F71.8) still reaches this prompt regardless — it lives in
            // LlmPromptBuilder.BuildUserContent, not here.
            var raw = await RequestCompletionAsync(cfg, request, personaOverride, card: null, ct);
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
        LlmOptions cfg, SegmentRequest request, Persona? persona, PersonaCard? card, CancellationToken ct)
    {
        // Captured up front, once, for LlmCallRing (SPEC F73.1, T41) — startedAt mirrors
        // LlmCopyStatusHolder's own attemptedAt semantics (includes any single-flight queueing wait
        // below), and mode is read fresh right here rather than threaded in as a parameter: a
        // preview call never passes through DegradationGatedCopyWriter (SPEC F69.4), so there is no
        // caller-evaluated mode available for that path, and reading IDegradationModeReader
        // uniformly for every path keeps this the one recording point instead of two.
        var startedAt = timeProvider.GetUtcNow();
        var mode = degradationMode.CurrentMode;

        // Hoisted above the try (mirrors WriteAsync's own cfg/persona hoisting, T3 review finding)
        // so a fault EARLIER than prompt assembly (e.g. a malformed endpoint URI) still lets the
        // catch-all below record whatever prompt context existed at that point — null here, in that
        // one early case.
        string? systemPrompt = null;
        string? userPrompt = null;

        // Single-flight (SPEC F69.6, gh-#36): acquired BEFORE the per-call timeout clock starts, so
        // a caller queued behind another generation is waiting its turn, not burning its own
        // Llm:TimeoutSeconds budget — and a caller whose own ct cancels while still queued (e.g.
        // shutdown) throws right out of WaitAsync without ever holding the gate (and without ever
        // reaching LlmCallRing.Record below — nothing was actually attempted).
        await singleFlight.WaitAsync(ct);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

            var http = httpClientFactory.CreateClient(HttpClientName);

            // Built before the request goes out (moved ahead of EndpointUri.Combine, T41 review
            // finding) so systemPrompt/userPrompt are available to the ring for every failure this
            // method can raise, not just the ones after prompt assembly.
            systemPrompt = LlmPromptBuilder.BuildSystemPrompt(LlmPromptBuilder.BuildPersonaSection(persona, card));
            userPrompt = LlmPromptBuilder.BuildUserContent(request, LlmPromptBuilder.BuildStationClockLine(timeProvider));

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
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
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
            var text = payload?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

            // Ok records the RAW reply (SPEC F73.1) — a later CleanCopy rejection (empty/over-length)
            // is a hygiene decision the caller makes, not a fact about whether the call itself
            // succeeded; see LlmCallOutcome.Ok's own remarks.
            callRing.Record(
                systemPrompt, userPrompt, text, startedAt, ElapsedMs(startedAt),
                LlmCallOutcome.Ok, statusDetail: null, mode);
            return text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled (e.g. shutdown) — not our own Llm:TimeoutSeconds budget expiring,
            // and not a call outcome worth a ring entry either (mirrors WriteAsync's/WritePreviewAsync's
            // own handling of this exact case). Propagate untouched.
            throw;
        }
        catch (Exception ex)
        {
            var (outcome, detail) = ClassifyForRing(ex);
            callRing.Record(
                systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode);
            throw;
        }
        finally
        {
            singleFlight.Release();
        }
    }

    long ElapsedMs(DateTimeOffset startedAt) => (long)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;

    /// <summary>
    /// Classifies a completion fault for <see cref="LlmCallRing"/> (SPEC F73.1): the ONE other
    /// <see cref="OperationCanceledException"/> source reaching this catch-all (the caller's own
    /// cancellation is already filtered out by the clause above) is <c>RequestCompletionAsync</c>'s
    /// own <c>timeoutCts</c> firing — <see cref="LlmCallOutcome.Timeout"/>, distinct from a generic
    /// <see cref="LlmCallOutcome.Failed"/>. Deliberately independent of <see cref="LogFailure"/>'s
    /// own <c>detail</c> switch (SPEC F69.7) — that one feeds a WARN line and has no need to split
    /// out timeout, so duplicating this small a classification is simpler than threading a shared
    /// helper through two call sites with different needs.
    /// </summary>
    static (LlmCallOutcome Outcome, string Detail) ClassifyForRing(Exception ex) => ex switch
    {
        OperationCanceledException => (LlmCallOutcome.Timeout, "Llm:TimeoutSeconds exceeded"),
        HttpRequestException { StatusCode: { } status } => (LlmCallOutcome.Failed, $"HTTP {(int)status}"),
        _ => (LlmCallOutcome.Failed, ex.GetType().Name),
    };

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
