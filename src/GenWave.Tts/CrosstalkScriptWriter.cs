namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.Logging;
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
/// <b>Quirks (SPEC F218.7, PLAN T688).</b> Crosstalk is stocked ahead, so an exchange can air in a later
/// shift than the one it was written in, and it may be discarded unaired. A <c>maxPerShift</c> spend
/// written here would be wrong either way, so the selection carries no persona or shift key: capped
/// quirks are excluded and nothing is spent. Plain quirks are still sampled (F71.3); capped ones belong
/// to patter, whose render airs in its own shift.
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
    TimeProvider timeProvider,
    IQuirkSelector? quirkSelector = null,
    IRepetitionCheck? repetitionCheck = null, // T694: absent means no per-speaker check, as before
    IUtteranceStore? utteranceStore = null, // T694: absent means no stored lines
    IStationEventSink? events = null, // T694: absent means no repeat-caught rows
    ISentenceTrimmer? sentenceTrimmer = null) // T715: absent means a repeated line always re-asks, then drops
{
    readonly CrosstalkLineMemory memory = new(
        repetitionCheck, utteranceStore, events ?? NoOpStationEventSink.Instance, logger, sentenceTrimmer);

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

        var hostQuirks = await SelectQuirksAsync(request.HostCard, ct);
        var neighborQuirks = await SelectQuirksAsync(request.NeighborCard, ct);

        var systemPrompt = CrosstalkPromptBuilder.BuildSystemPrompt(
            request.HostCard, request.NeighborCard, durationTargetSeconds, request.StationLocalNow,
            hostQuirks, neighborQuirks);
        var userPrompt = CrosstalkPromptBuilder.BuildUserContent(
            request, LlmPromptBuilder.BuildStationClockLine(request.StationLocalNow));

        var histories = await memory.ReadHistoriesAsync(request, ct);

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
            var cap = DeriveScriptGenerationCap(durationTargetSeconds);
            var (raw, result) = await PostAndParseAsync(
                http, requestUri, cfg, systemPrompt, userPrompt, cap, request, durationTargetSeconds, timeoutCts.Token);

            if (result is CrosstalkWriteResult.Accepted first)
            {
                var hits = memory.FindRepeats(first.Script, request.StationName, histories);
                var trimmedFirst = hits.Count > 0 ? memory.Trim(first.Script, hits) : null;
                if (trimmedFirst is not null)
                    memory.PublishFirstPass(hits, trimmedFirst);

                if (trimmedFirst is { Emptied: 0 })
                {
                    // SPEC F218.36: every repeated line kept a fresh sentence, so no second call is made.
                    result = new CrosstalkWriteResult.Accepted(new CrosstalkAiredScript(trimmedFirst.Lines));
                }
                else if (trimmedFirst is not null)
                {
                    // SPEC F218.12/F218.14: exactly ONE re-ask, never a third call. The caught call gets its
                    // own honest ring entry BEFORE the re-ask fires.
                    recorder.Record(
                        personaName, systemPrompt, userPrompt, raw, startedAt, ElapsedMs(startedAt),
                        LlmCallOutcome.Ok, statusDetail: null, mode, LlmCallCause.Repeat, cfg.Model, LlmCallKind.Crosstalk);

                    // userPrompt/startedAt are REASSIGNED so a fault in the re-ask is attributed to it.
                    userPrompt = LlmPromptBuilder.BuildRepeatReAsk(
                        userPrompt, hits.SelectMany(h => h.Phrases).Distinct(StringComparer.Ordinal).ToList());
                    startedAt = timeProvider.GetUtcNow();
                    (raw, result) = await PostAndParseAsync(
                        http, requestUri, cfg, systemPrompt, userPrompt, cap, request, durationTargetSeconds, timeoutCts.Token);
                    if (result is CrosstalkWriteResult.Accepted reasked)
                        result = DropStillRepeating(reasked, request, histories);
                }
            }

            switch (result)
            {
                case CrosstalkWriteResult.Accepted accepted:
                    await memory.StoreAsync(request, accepted.Script, ct);
                    return Accept(accepted, personaName, systemPrompt, userPrompt, raw, startedAt, mode, cfg.Model);
                // discarded.Cause was decided once, at the source (SPEC F139.1) — never re-derived from its text.
                case CrosstalkWriteResult.Discarded discarded:
                    return Discard(
                        discarded.Reason, discarded.Cause, personaName, startedAt, mode, systemPrompt, userPrompt, cfg.Model, raw);
                default:
                    throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(CrosstalkWriteResult)} case.");
            }
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

    /// <summary>
    /// One completion: post, refuse a length-truncated reply (SPEC F127.4, F127.11, gh-#424 class — checked
    /// BEFORE Parse, since a chopped last line can still parse), then parse. The raw reply rides along for the ring.
    /// </summary>
    async Task<(string Raw, CrosstalkWriteResult Result)> PostAndParseAsync(
        HttpClient http, Uri requestUri, LlmOptions cfg, string systemPrompt, string userPrompt, int cap,
        CrosstalkExchangeRequest request, int durationTargetSeconds, CancellationToken ct)
    {
        // The wire call is LlmCopyWriter.PostChatCompletionAsync, the ONE seam this writer and AdScriptWriter share.
        var reply = await LlmCopyWriter.PostChatCompletionAsync(http, requestUri, cfg, systemPrompt, userPrompt, cap, ct);
        var raw = reply.Content;
        if (reply.FinishReason == "length")
        {
            return (raw, new CrosstalkWriteResult.Discarded(
                "the completion was cut short by max_tokens (finish_reason: length) — a truncated reply is never aired",
                LlmCallCause.OverLength));
        }

        return (raw, CrosstalkScriptParser.Parse(
            raw, cfg.MaxCopyChars, durationTargetSeconds, request.StationLocalNow, request.StationName));
    }

    /// <summary>
    /// SPEC F218.14, F218.36: after the one re-ask, a line that still repeats its own speaker is trimmed to its
    /// fresh sentences, or dropped from the exchange when none is left — never replaced by template text. The
    /// survivors must still be a two-voice exchange of at least <see cref="CrosstalkScriptParser.MinLines"/> lines,
    /// else the whole exchange is discarded as <see cref="LlmCallCause.Repeat"/>. The repeat-caught rows are
    /// published after those checks (Trimmed and Dropped per line when it airs, all Dropped when it is discarded).
    /// Alternation is deliberately not re-checked: dropping one of A's lines from H N H N leaves two N lines in a
    /// row (AC3 forces this), and the assembler handles a same-speaker pair with the narrower F198.3 gap; a
    /// leading interjection still starts at 0 and its marker never reaches TTS.
    /// </summary>
    CrosstalkWriteResult DropStillRepeating(
        CrosstalkWriteResult.Accepted reasked, CrosstalkExchangeRequest request,
        IReadOnlyList<CrosstalkLineMemory.SpeakerHistory> histories)
    {
        var hits = memory.FindRepeats(reasked.Script, request.StationName, histories);
        if (hits.Count == 0)
            return reasked;

        // SPEC F218.36/F218.38: a line with a fresh sentence left is trimmed and kept; one left empty is dropped.
        // The rows are published only after the survivor checks, so they are true about what airs.
        var trimmed = memory.Trim(reasked.Script, hits);
        var survivors = trimmed.Lines;

        var discard = SurvivorDiscardReason(survivors);
        memory.PublishAfterReAsk(hits, trimmed, exchangeAirs: discard is null);
        if (discard is not null)
            return new CrosstalkWriteResult.Discarded(discard, LlmCallCause.Repeat);

        return new CrosstalkWriteResult.Accepted(new CrosstalkAiredScript(survivors));
    }

    static string? SurvivorDiscardReason(IReadOnlyList<CrosstalkAiredLine> survivors)
    {
        if (survivors.Count < CrosstalkScriptParser.MinLines)
            return $"{survivors.Count} lines were left after dropping repeated ones; at least {CrosstalkScriptParser.MinLines} are needed";

        return CrosstalkScriptParser.MissingSpeakerReason(survivors) is { } missing
            ? $"after dropping repeated lines, {missing}"
            : null;
    }

    /// <summary>
    /// One card's quirks with no persona or shift key (spend: true). That is the IQuirkSelector contract's
    /// on-air-untracked path: it excludes capped quirks and writes no spend row. Do not "fix" it to
    /// spend: false, which is the preview path and would roll capped quirks in.
    /// Null (no selector wired) lets the prompt builder sample plain. A selector fault costs this card its quirks, never the exchange.
    /// </summary>
    async Task<QuirkSelection?> SelectQuirksAsync(PersonaCard card, CancellationToken ct)
    {
        if (quirkSelector is null)
            return null;

        try
        {
            return await quirkSelector.SelectAsync(personaId: null, card, shiftKey: null, spend: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Quirk selection failed for crosstalk card {Card}; it airs without quirks",
                LogSanitize.Strip(card.Name));
            return QuirkSelection.None;
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
