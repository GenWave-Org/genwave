namespace GenWave.Tts;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Generates one two-voice banter exchange per call (SPEC F127.3, F127.4, STORY-326) — the
/// GenWave.Tts half of the crosstalk feature (ARCHITECTURE.md "Crosstalk (F127…)"): casting
/// (<c>CrosstalkPlanner</c>), per-line rendering, and assembly are ALL later tasks (T284-T287) this
/// class never touches. Lives beside <see cref="LlmCopyWriter"/> and shares its F123 machinery
/// (<see cref="LlmCopyWriter.DeriveMaxTokens"/>, <see cref="LlmCopyWriter.ApplyCopyHygiene"/> via
/// <see cref="CrosstalkScriptParser"/>, <see cref="LlmCallRing"/>) rather than duplicating it — see
/// each reused member's own remarks for exactly why sharing that piece is safe.
///
/// <para>
/// <b>One completion, whole exchange (SPEC F127.3).</b> Unlike <see cref="LlmCopyWriter"/>, this
/// writer NEVER degrades to a template — F127.4 is skip-only: any failure (disabled endpoint,
/// transport fault, a completion truncated at <c>max_tokens</c>, malformed reply, a line failing
/// hygiene/budget, an over-target duration estimate) returns <see cref="CrosstalkWriteResult.Discarded"/>
/// with one reason, logged at Information
/// (never WARN — banter is optional color, a miss is not an outage) and recorded into
/// <see cref="LlmCallRing"/> under <see cref="LlmCallKind.Crosstalk"/> so <c>/api/llm-calls</c> can
/// answer "why was there no banter" (SPEC F127.11) exactly the way it already answers "why was there
/// no lead-in" for <see cref="LlmCopyWriter"/>.
/// </para>
///
/// <para>
/// <b>No single-flight gate here (unlike <see cref="LlmCopyWriter"/>'s own SPEC F69.6 seam).</b>
/// Crosstalk generation happens entirely off the on-air clock, ahead of air (SPEC F127.7) — a LATER
/// task's thin stock-timer loop (T286) is what will pace how often this writer is actually called,
/// which is the natural place to coordinate backend concurrency with the on-air copy path if that
/// ever proves necessary; adding a shared gate here now, with no caller yet, would be speculative.
/// </para>
///
/// <para>
/// Registered as a DI singleton with NO eager I/O in its constructor (Story125's zero-I/O invariant)
/// — every dependency here is itself a cheap seam (an options monitor, a ring, a logger, an
/// <see cref="IHttpClientFactory"/>), so constructing this class never touches the network.
/// </para>
/// </summary>
public sealed class CrosstalkScriptWriter(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<LlmOptions> llmOptions,
    IOptionsMonitor<CrosstalkOptions> crosstalkOptions,
    LlmCallRing callRing,
    IDegradationModeReader degradationMode,
    ILogger<CrosstalkScriptWriter> logger,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Requests one exchange. Never throws toward the caller for anything short of the caller's own
    /// <paramref name="ct"/> cancelling — every other fault (disabled endpoint, timeout, non-2xx,
    /// connect, malformed/invalid script) resolves to <see cref="CrosstalkWriteResult.Discarded"/>
    /// (SPEC F127.4's skip-only failure mode).
    /// </summary>
    public async Task<CrosstalkWriteResult> WriteExchangeAsync(CrosstalkExchangeRequest request, CancellationToken ct)
    {
        var startedAt = timeProvider.GetUtcNow();
        var mode = degradationMode.CurrentMode;
        var personaName = $"{request.HostCard.Name} / {request.NeighborCard.Name}";

        var cfg = llmOptions.CurrentValue;
        if (string.IsNullOrEmpty(cfg.Endpoint))
            return Discard("Llm:Endpoint is not configured", personaName, startedAt, mode, systemPrompt: null, userPrompt: null);

        var durationTargetSeconds = crosstalkOptions.CurrentValue.DurationTargetSeconds;

        var systemPrompt = CrosstalkPromptBuilder.BuildSystemPrompt(request.HostCard, request.NeighborCard, cfg.MaxCopyChars);
        var userPrompt = CrosstalkPromptBuilder.BuildUserContent(
            request, LlmPromptBuilder.BuildStationClockLine(request.StationLocalNow));

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

            var http = httpClientFactory.CreateClient(LlmCopyWriter.HttpClientName);
            var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");

            var body = new
            {
                model = cfg.Model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                // SPEC F127.3: "the F123.1 derived generation cap applies to the whole script" — the
                // SAME formula LlmCopyWriter derives an ordinary blurb's cap from, not a second,
                // banter-specific one (the one-knob discipline).
                max_tokens = LlmCopyWriter.DeriveMaxTokens(cfg.MaxCopyChars),
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(body),
            };

            if (!string.IsNullOrEmpty(cfg.ApiKey))
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);

            var response = await http.SendAsync(httpRequest, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(timeoutCts.Token);
            var choice = payload?.Choices?.FirstOrDefault();
            var raw = choice?.Message?.Content ?? string.Empty;

            // SPEC F127.4, F127.11 (gh-#424 class, one seam over): a completion the backend cut
            // short at its own max_tokens cap leaves a truncated last line that can still PARSE
            // cleanly (a chopped sentence still matches the HOST:/NEIGHBOR: line shape) and would
            // otherwise air mid-word — so this is checked BEFORE Parse ever runs, not left for the
            // parser to maybe catch by accident. finish_reason is the OpenAI/ollama-compatible
            // signal for exactly this (see ChatCompletionChoice.FinishReason's own remarks); "stop"
            // (or a missing field, from an endpoint that predates it) never trips this check.
            if (choice?.FinishReason == "length")
            {
                return Discard(
                    "the completion was cut short by max_tokens (finish_reason: length) — a truncated reply is never aired",
                    personaName, startedAt, mode, systemPrompt, userPrompt, raw);
            }

            var result = CrosstalkScriptParser.Parse(raw, cfg.MaxCopyChars, durationTargetSeconds);
            return result switch
            {
                CrosstalkWriteResult.Accepted => Accept(result, personaName, systemPrompt, userPrompt, raw, startedAt, mode),
                CrosstalkWriteResult.Discarded discarded => Discard(
                    discarded.Reason, personaName, startedAt, mode, systemPrompt, userPrompt, raw),
                _ => throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(CrosstalkWriteResult)} case."),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled (e.g. shutdown) — not our own Llm:TimeoutSeconds budget expiring,
            // and not a call outcome worth a ring entry (mirrors LlmCopyWriter's own handling).
            throw;
        }
        catch (Exception ex)
        {
            var (outcome, detail) = LlmCopyWriter.ClassifyForRing(ex);
            callRing.Record(
                personaName, systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode, LlmCallKind.Crosstalk);
            logger.LogInformation(
                "Crosstalk exchange discarded (persona: {PersonaName}): {Detail}",
                personaName.ReplaceLineEndings(" "), detail.ReplaceLineEndings(" "));
            return new CrosstalkWriteResult.Discarded(detail);
        }
    }

    CrosstalkWriteResult Accept(
        CrosstalkWriteResult result, string personaName, string systemPrompt, string userPrompt, string raw,
        DateTimeOffset startedAt, DegradationMode mode)
    {
        callRing.Record(
            personaName, systemPrompt, userPrompt, raw, startedAt, ElapsedMs(startedAt),
            LlmCallOutcome.Ok, statusDetail: null, mode, LlmCallKind.Crosstalk);
        return result;
    }

    /// <summary>
    /// The one discard path every failure funnels through (SPEC F127.4) — records into
    /// <see cref="LlmCallRing"/> (skipped entirely when <paramref name="systemPrompt"/> is null, i.e.
    /// nothing was ever attempted — the disabled-endpoint short-circuit, mirroring
    /// <see cref="LlmCopyWriter.WriteAsync"/>'s own "disabled means no ring entry" posture) and logs
    /// exactly one Information line (never WARN — F127.4's own posture: a discard is discipline, not
    /// an outage).
    /// </summary>
    CrosstalkWriteResult.Discarded Discard(
        string reason, string personaName, DateTimeOffset startedAt, DegradationMode mode,
        string? systemPrompt, string? userPrompt, string? raw = null)
    {
        if (systemPrompt is not null)
        {
            callRing.Record(
                personaName, systemPrompt, userPrompt, raw, startedAt, ElapsedMs(startedAt),
                LlmCallOutcome.Rejected, reason, mode, LlmCallKind.Crosstalk);
        }

        logger.LogInformation(
            "Crosstalk exchange discarded (persona: {PersonaName}): {Reason}",
            personaName.ReplaceLineEndings(" "), reason.ReplaceLineEndings(" "));
        return new CrosstalkWriteResult.Discarded(reason);
    }

    long ElapsedMs(DateTimeOffset startedAt) => (long)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
}
