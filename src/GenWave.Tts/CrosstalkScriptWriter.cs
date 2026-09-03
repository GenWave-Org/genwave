namespace GenWave.Tts;

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
/// hygiene/budget, an over-target duration estimate, a F138.6 truth-gate violation) returns
/// <see cref="CrosstalkWriteResult.Discarded"/>
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
    LlmCallRecorder recorder,
    IDegradationModeReader degradationMode,
    ILogger<CrosstalkScriptWriter> logger,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Headroom the whole-script generation cap carries OVER <see cref="CrosstalkOptions.DurationTargetSeconds"/>'s
    /// own char estimate (SPEC F127.3, T283 paper-audition reconciliation, gh-#385). The cap exists
    /// to stop runaway rambling, not to enforce the duration target itself —
    /// <see cref="CrosstalkScriptParser.Parse"/>'s own estimated-duration check is what does that
    /// quality rejection, AFTER a clean (untruncated) completion has already come back. A cap that
    /// bites right at the target would truncate scripts the duration gate would have ACCEPTED —
    /// and a truncated reply is never airable regardless of how it reads (it can still parse
    /// cleanly, which is exactly why the <c>finish_reason: length</c> check discards it before
    /// <see cref="CrosstalkScriptParser.Parse"/> ever runs — see that check's own remarks). ~2x
    /// leaves room for speaker-tag/newline overhead and ordinary model variance without turning
    /// the cap into a second, tighter duration gate.
    /// </summary>
    const double GenerationCapHeadroomMultiplier = 2.0;

    /// <summary>
    /// Derives the whole-script <c>max_tokens</c> cap from <see cref="CrosstalkOptions.DurationTargetSeconds"/>
    /// (SPEC F127.3, T283 paper-audition reconciliation, gh-#385) — the ONLY setting this reads;
    /// <see cref="LlmOptions.MaxCopyChars"/> stays the per-LINE budget <see cref="CrosstalkScriptParser.Parse"/>
    /// enforces and never reaches this formula. The first live run against llama3.2:3b proved the
    /// PRIOR blurb-scaled derivation (before this method existed, the request site called
    /// <see cref="LlmCopyWriter.DeriveMaxTokens"/> straight on <c>Llm:MaxCopyChars</c>, sized for one
    /// short line) starves a 3-8 line script: 4 of 8 attempts died to <c>finish_reason: length</c>
    /// before a single reply could even reach the parser. Multiplies the duration target's own char
    /// estimate (<see cref="CrosstalkScriptParser.CharsPerSecond"/> — the SAME spoken-rate constant
    /// the parser's own over-duration check already applies to an accepted script) by
    /// <see cref="GenerationCapHeadroomMultiplier"/>, then hands that figure to
    /// <see cref="LlmCopyWriter.DeriveMaxTokens"/> — reusing that method's chars-to-tokens shape
    /// (divisor, floor, ceiling) rather than a second, independently-tuned formula.
    /// </summary>
    static int DeriveScriptGenerationCap(int durationTargetSeconds)
    {
        var headroomChars = durationTargetSeconds * CrosstalkScriptParser.CharsPerSecond * GenerationCapHeadroomMultiplier;
        return LlmCopyWriter.DeriveMaxTokens((int)headroomChars);
    }

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
        {
            // SPEC F140 review finding F3: this is the archetypal pre-flight refusal — zero I/O,
            // resolves in microseconds — so it carries GenerationAttempted: false (see
            // CrosstalkWriteResult.Discarded's own remarks for why a pacing caller cares).
            //
            // SPEC F139.1 (T330 review advisory): Cause is a DELIBERATE ConnectionFailure here, not a
            // default filled just to satisfy the parameter — systemPrompt: null skips the ring/counter
            // record below (nothing was ever attempted, mirroring GenerationAttempted: false above),
            // so this value only ever surfaces on the returned Discarded itself, never on a ring row.
            // ConnectionFailure is the honest answer regardless: an unset Llm:Endpoint IS "nowhere
            // configured to connect to" — a connection-layer fact, not a shape or a timeout one.
            return Discard(
                "Llm:Endpoint is not configured", LlmCallCause.ConnectionFailure, personaName, startedAt, mode,
                systemPrompt: null, userPrompt: null, cfg.Model, generationAttempted: false);
        }

        var durationTargetSeconds = crosstalkOptions.CurrentValue.DurationTargetSeconds;

        var systemPrompt = CrosstalkPromptBuilder.BuildSystemPrompt(
            request.HostCard, request.NeighborCard, durationTargetSeconds, request.StationLocalNow);
        var userPrompt = CrosstalkPromptBuilder.BuildUserContent(
            request, LlmPromptBuilder.BuildStationClockLine(request.StationLocalNow));

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

            var http = httpClientFactory.CreateClient(LlmCopyWriter.HttpClientName);
            var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");

            // PLAN T400 review F5: the wire call itself (body/header/parse) is
            // LlmCopyWriter.PostChatCompletionAsync — the ONE seam this writer and AdScriptWriter both
            // call, rather than each hand-rolling its own copy (a third hand-rolled copy is what
            // caused that review's own F1). SPEC F127.3 (T283 paper-audition reconciliation, gh-#385):
            // the cap derives from Crosstalk:DurationTargetSeconds — the ONE knob that already
            // describes a whole exchange — not from Llm:MaxCopyChars (that stayed blurb-scaled and
            // starved a multi-line script on the first live run; see DeriveScriptGenerationCap's own
            // remarks). Still reuses LlmCopyWriter.DeriveMaxTokens's chars-to-tokens shape
            // (divisor/floor/ceiling) rather than a second, independently-tuned formula.
            var reply = await LlmCopyWriter.PostChatCompletionAsync(
                http, requestUri, cfg, systemPrompt, userPrompt, DeriveScriptGenerationCap(durationTargetSeconds),
                timeoutCts.Token);
            var raw = reply.Content;

            // SPEC F127.4, F127.11 (gh-#424 class, one seam over): a completion the backend cut
            // short at its own max_tokens cap leaves a truncated last line that can still PARSE
            // cleanly (a chopped sentence still matches the HOST:/NEIGHBOR: line shape) and would
            // otherwise air mid-word — so this is checked BEFORE Parse ever runs, not left for the
            // parser to maybe catch by accident. finish_reason is the OpenAI/ollama-compatible
            // signal for exactly this (see ChatCompletionChoice.FinishReason's own remarks); "stop"
            // (or a missing field, from an endpoint that predates it) never trips this check.
            if (reply.FinishReason == "length")
            {
                return Discard(
                    "the completion was cut short by max_tokens (finish_reason: length) — a truncated reply is never aired",
                    LlmCallCause.OverLength, personaName, startedAt, mode, systemPrompt, userPrompt, cfg.Model, raw);
            }

            var result = CrosstalkScriptParser.Parse(
                raw, cfg.MaxCopyChars, durationTargetSeconds, request.StationLocalNow, request.StationName);
            return result switch
            {
                CrosstalkWriteResult.Accepted => Accept(
                    result, personaName, systemPrompt, userPrompt, raw, startedAt, mode, cfg.Model),
                // discarded.Cause was decided once, at the source, inside CrosstalkScriptParser.Parse's
                // own reject branches (SPEC F139.1) — never re-derived here from discarded.Reason's text.
                CrosstalkWriteResult.Discarded discarded => Discard(
                    discarded.Reason, discarded.Cause, personaName, startedAt, mode, systemPrompt, userPrompt, cfg.Model, raw),
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
            var (outcome, cause, detail) = LlmCopyWriter.ClassifyForRing(ex);

            // SPEC F140 review finding F3: an HttpRequestException with no StatusCode is .NET's own
            // signal that no response was ever received (a connect refusal, DNS failure, TLS
            // failure — thrown by SendAsync itself, before EnsureSuccessStatusCode ever runs) —
            // milliseconds, no generation attempted. Every other fault reaching this catch block
            // (a timeout waiting the full Llm:TimeoutSeconds, a non-2xx status AFTER a response
            // arrived, a malformed body) represents genuine wall-clock time spent on a real attempt,
            // so it keeps GenerationAttempted's own default of true.
            var generationAttempted = ex is not HttpRequestException { StatusCode: null };

            recorder.Record(
                personaName, systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode, cause, cfg.Model, LlmCallKind.Crosstalk);
            logger.LogInformation(
                "Crosstalk exchange discarded (persona: {PersonaName}): {Detail}",
                personaName.ReplaceLineEndings(" "), detail.ReplaceLineEndings(" "));
            return new CrosstalkWriteResult.Discarded(detail, cause, generationAttempted);
        }
    }

    CrosstalkWriteResult Accept(
        CrosstalkWriteResult result, string personaName, string systemPrompt, string userPrompt, string raw,
        DateTimeOffset startedAt, DegradationMode mode, string model)
    {
        recorder.Record(
            personaName, systemPrompt, userPrompt, raw, startedAt, ElapsedMs(startedAt),
            LlmCallOutcome.Ok, statusDetail: null, mode, LlmCallCause.Success, model, LlmCallKind.Crosstalk);
        return result;
    }

    /// <summary>
    /// The one discard path every failure funnels through (SPEC F127.4) — records into
    /// <see cref="LlmCallRecorder"/> (skipped entirely when <paramref name="systemPrompt"/> is null,
    /// i.e. nothing was ever attempted — the disabled-endpoint short-circuit, mirroring
    /// <see cref="LlmCopyWriter.WriteAsync"/>'s own "disabled means no ring entry" posture) and logs
    /// exactly one Information line (never WARN — F127.4's own posture: a discard is discipline, not
    /// an outage). <paramref name="cause"/> (SPEC F139.1, PLAN T330) is decided by the CALLER, at the
    /// point it already knows why — this method never inspects <paramref name="reason"/>'s text.
    /// </summary>
    CrosstalkWriteResult.Discarded Discard(
        string reason, LlmCallCause cause, string personaName, DateTimeOffset startedAt, DegradationMode mode,
        string? systemPrompt, string? userPrompt, string model, string? raw = null, bool generationAttempted = true)
    {
        if (systemPrompt is not null)
        {
            recorder.Record(
                personaName, systemPrompt, userPrompt, raw, startedAt, ElapsedMs(startedAt),
                LlmCallOutcome.Rejected, reason, mode, cause, model, LlmCallKind.Crosstalk);
        }

        logger.LogInformation(
            "Crosstalk exchange discarded (persona: {PersonaName}): {Reason}",
            personaName.ReplaceLineEndings(" "), reason.ReplaceLineEndings(" "));
        return new CrosstalkWriteResult.Discarded(reason, cause, generationAttempted);
    }

    long ElapsedMs(DateTimeOffset startedAt) => (long)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
}
