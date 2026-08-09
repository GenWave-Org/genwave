namespace GenWave.Tts;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// LLM-backed <see cref="ISegmentCopyWriter"/> (SPEC F34.2-F34.5, F92.2, F92.5, F107.3): authors
/// copy for exactly the kinds <see cref="IsLlmAuthored"/> reports true for — <see cref="SegmentKind.LeadIn"/>,
/// <see cref="SegmentKind.BackAnnounce"/>, <see cref="SegmentKind.SignOff"/>,
/// <see cref="SegmentKind.SignOn"/>, and, as of T224, <see cref="SegmentKind.ContextSegment"/> —
/// from an OpenAI-compatible chat-completions endpoint. <see cref="SegmentKind.StationId"/> and
/// <see cref="SegmentKind.TimeDate"/> always delegate straight to <paramref name="fallback"/> with
/// zero HTTP — brand/time copy stays fixed and forever-cached.
/// Enabled-ness and every other option are read from <paramref name="optionsMonitor"/> fresh on each
/// call (F36.2) — an empty <c>Llm:Endpoint</c> means disabled. Any failure (disabled, timeout,
/// non-2xx, connect, empty/over-length copy) degrades to <paramref name="fallback"/>'s template copy
/// with exactly one WARN; this writer never throws toward
/// <see cref="GenWave.Core.Abstractions.ITtsSegmentSource"/> (F12.4 extended).
///
/// <paramref name="personaAccessor"/> is resolved once per LeadIn/BackAnnounce/SignOff/SignOn render
/// (SPEC F35.2, F35.3, F71.3) — never for the templated kinds or a disabled writer — both for the
/// legacy <c>Persona</c> row AND its card counterpart, composing an appended soul + sampled-quirks
/// section onto the baked system prompt (see <see cref="LlmPromptBuilder.BuildPersonaSection"/>). No
/// persona active, and no soul/quirks to show, leaves the prompt exactly as it was before T6 (F35.2 —
/// blurbs work persona-less).
///
/// <para>
/// The DJ's clock (SPEC F71.8, gh-#13, STORY-193): every prompt this writer builds — persona
/// active or not — carries the current date/weekday/time in station-local terms (see
/// <see cref="LlmPromptBuilder.BuildStationClockLine"/>), so the model answers from the injected
/// clock rather than inventing one. "Station-local" resolves through
/// <paramref name="stationClock"/> (gh-#117 — <c>Station:Timezone</c> when configured, read live
/// per render); a composition that supplies none (tests, pre-gh-#117 rigs) falls back to
/// <paramref name="timeProvider"/>'s own <see cref="TimeProvider.LocalTimeZone"/> — the
/// container's clock, byte-identical to the prior behavior.
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
/// in-memory ring: the active persona's name (gh-#429), prompt, raw response, timing, outcome, and
/// the degradation mode active at call time
/// (<see cref="IDegradationModeReader.CurrentMode"/>, read fresh right here rather than passed
/// in — a preview never passes through <see cref="DegradationGatedCopyWriter"/>, so there is no
/// caller-supplied mode to reuse for that path; reading it uniformly for every path keeps this the
/// one recording point instead of two). Never logged, never persisted — see <see cref="LlmCallRing"/>'s
/// own remarks.
/// </para>
///
/// <para>
/// The anti-repetition posture (SPEC F83.1, STORY-214, T65): <see cref="previousBreakTasteNotes"/>
/// remembers the immediately preceding ON-AIR break's fired-rule descriptions, in-memory, so
/// <see cref="LlmPromptBuilder.BuildUserContent"/> can ask for different phrasing when the SAME
/// taste note would otherwise land twice in a row. Deliberately NOT F71.4's <c>persona_memory</c>
/// recall windows — that is a Postgres-backed, cross-restart "kind:bit" callback system for
/// narrative bits/jokes that this writer never touches today; reaching for it here would be new
/// architecture wired in for one prompt marker, not reuse.
///
/// Both the read and the write live INSIDE <see cref="RequestCompletionAsync"/>'s own single-flight
/// critical section, not around it (T65 review finding): <c>Orchestrator.EnqueuePatterAsync</c>
/// starts one unit's BackAnnounce and LeadIn renders concurrently — both TTS renders are kicked off
/// before either is awaited — so a read/write pair taken OUTSIDE the gate could have the second
/// render capture the snapshot from before the first render's own write ever lands, and the two
/// would never compare against each other. Doing both only once <see cref="RequestCompletionAsync"/>
/// actually holds <see cref="singleFlight"/> guarantees the second of any concurrent pair always
/// sees the first's freshly-written notes. Read only, and written only, when the call originates
/// from <see cref="WriteAsync"/> (<c>updateTasteMemory: true</c>) — <see cref="WritePreviewAsync"/>
/// passes <c>updateTasteMemory: false</c> and always renders against an empty list, so auditioning a
/// persona in the admin UI can never perturb what the NEXT real on-air break considers "recently
/// voiced".
/// </para>
///
/// <para>
/// The patter lane's single fact slot (SPEC F107.5, STORY-214, PLAN T225): exactly like
/// <see cref="previousBreakTasteNotes"/> above, this is an on-air-only concern with its own
/// dedicated take point — see <see cref="TakeDuePatterFactForOnAirRender"/>'s remarks for exactly
/// where that take happens and why <see cref="WritePreviewAsync"/> can never trigger it. Unlike the
/// taste-note field, there is no writer-owned STATE here at all — <c>IContextPatterFactSource</c>
/// (<paramref name="patterFactSource"/>) already owns the one slot per cadence window; this class
/// only ever reads it, once, per eligible on-air render.
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
    IDegradationModeReader degradationMode,
    IStationClockProvider? stationClock = null,
    IContextPatterFactSource? patterFactSource = null) : ISegmentCopyWriter, IPersonaPreviewWriter
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

    /// <summary>
    /// SPEC F83.1 (STORY-214, PLAN T65) — the previous ON-AIR break's fired-rule descriptions (see
    /// <see cref="LlmPromptBuilder.DescribeFiredRules"/>). Read and written EXCLUSIVELY inside
    /// <see cref="RequestCompletionAsync"/>'s own single-flight critical section (T65 review
    /// finding) — never out here in <see cref="WriteAsync"/> — see the class remarks above for why:
    /// two renders belonging to the same unit are started concurrently, so a read/write pair taken
    /// outside <see cref="singleFlight"/>'s gate would race. Only touched when that call's
    /// <c>updateTasteMemory</c> parameter is true (an on-air <see cref="WriteAsync"/> call); a
    /// <see cref="WritePreviewAsync"/> call passes <c>false</c> and never reads or writes it. Starts
    /// empty (first-ever break has no "previous" to avoid repeating).
    /// </summary>
    IReadOnlyList<string> previousBreakTasteNotes = [];

    static readonly Regex NewlinePattern = new(@"\r\n|\r|\n", RegexOptions.Compiled);
    static readonly Regex BracketStageDirectionPattern = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    // A single word wrapped in one asterisk on each side (no internal spaces) reads as a stage
    // direction — *chuckles*, *laughs* — and is dropped whole. A multi-word wrap (the common
    // markdown emphasis shape, "*Next up*"/"**Next up**") survives this pass and loses only its
    // delimiters below.
    static readonly Regex AsteriskStageDirectionPattern = new(@"\*[^\s*]+\*", RegexOptions.Compiled);
    static readonly Regex MarkdownEmphasisPattern = new(@"[*_]+", RegexOptions.Compiled);
    static readonly Regex RepeatedWhitespacePattern = new(@"\s{2,}", RegexOptions.Compiled);

    // gh-#186: the meta words a model uses when talking ABOUT the copy instead of speaking it
    // ("Here's your lead-in copy:", "Sure, here you go:"). Deliberately NOT matched against
    // ordinary announcer phrasing — see StripChatPreamble's three-part gate; a preamble is only
    // dropped when one of these appears in it.
    static readonly Regex PreambleMetaWordPattern = new(
        @"(?i)\b(here[’']?s?|copy|response|sure|certainly|okay|lead[- ]in|back[- ]announce|announcement)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Single source of truth for exactly which <see cref="SegmentKind"/> values this writer calls
    /// the LLM for (SPEC F34.2, F92.2, F92.5, F107.3): the two track-anchored kinds (LeadIn,
    /// BackAnnounce), the two handoff kinds (SignOff, SignOn), and, as of T224,
    /// <see cref="SegmentKind.ContextSegment"/> — a provider's facts read as prose, never templated
    /// filler (F107.6: facts aren't airable as "Here's something worth knowing", so this kind has no
    /// templated rung that is ever allowed to reach air — see <c>TtsSegmentSource</c>'s own
    /// non-fresh-copy guard, extended alongside SignOff/SignOn for exactly that reason). Gates both
    /// <see cref="WriteAsync"/> and <see cref="WritePreviewAsync"/> so the two can never drift apart,
    /// and is the fact <see cref="LlmPromptBuilder.BuildSegmentLine"/>'s own exhaustiveness switch
    /// relies on staying in sync with (see that method's remarks): <see cref="SegmentKind.StationId"/>
    /// and <see cref="SegmentKind.TimeDate"/> are the only two kinds this reports false for, and they
    /// never reach a prompt at all.
    /// </summary>
    static bool IsLlmAuthored(SegmentKind kind) =>
        kind is SegmentKind.LeadIn or SegmentKind.BackAnnounce or SegmentKind.SignOff or SegmentKind.SignOn
            or SegmentKind.ContextSegment;

    public async Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct)
    {
        // StationId/TimeDate stay templated — brand/time copy must be crisp, consistent, and
        // forever-cacheable; the two track-anchored kinds (F34.2) and the two handoff kinds (F92.2,
        // F92.5) are the ones worth an LLM's while (see IsLlmAuthored, the single source of truth).
        if (!IsLlmAuthored(request.Kind))
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
            // SPEC F107.5 (STORY-298, PLAN T225) — the ONE place in this writer that may consume the
            // patter lane's due fact; see TakeDuePatterFactForOnAirRender's own remarks for why this
            // call lives HERE, in WriteAsync's own body, rather than inside the shared
            // RequestCompletionAsync below (which WritePreviewAsync also calls).
            var patterFact = TakeDuePatterFactForOnAirRender(request.Kind);
            // updateTasteMemory: true — this is an on-air call, so previousBreakTasteNotes is both
            // read and (on success) overwritten INSIDE RequestCompletionAsync's own single-flight
            // critical section (SPEC F83.1, T65 review finding); see that method's own remarks for
            // why the field can no longer be touched out here.
            var raw = await RequestCompletionAsync(
                cfg, request, persona, card, updateTasteMemory: true, patterFact, queueWaitBudget: null, ct);
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
    /// on-air <see cref="WriteAsync"/> path would have produced for the same request/persona.
    /// Deliberate differences: NOTHING here degrades to <paramref name="fallback"/> on an LLM miss
    /// for LeadIn/BackAnnounce/SignOff/SignOn — that would misrepresent the persona being auditioned — this method
    /// never records to <see cref="LlmCopyStatusHolder"/> (that holder tracks on-air attempts for
    /// <c>GET /api/status</c>; preview activity never airs and must not appear there), and it always
    /// passes <c>updateTasteMemory: false</c> to <see cref="RequestCompletionAsync"/> (SPEC F83.1,
    /// T65) — auditioning a card is not a real on-air break, so it neither reads nor perturbs
    /// <see cref="previousBreakTasteNotes"/>.
    /// </summary>
    public async Task<PersonaPreviewResult> WritePreviewAsync(
        SegmentRequest request, Persona? personaOverride, CancellationToken ct)
    {
        // StationId/TimeDate route straight to the template rung — mirrors WriteAsync's own
        // kind-based routing (F34.2, IsLlmAuthored). This is not a fallback: those two kinds never
        // call the LLM on-air either, so template text IS the correct preview for them.
        if (!IsLlmAuthored(request.Kind))
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
            //
            // patterFact: null, ALWAYS (SPEC F107.5, PLAN T225) — this method never calls
            // TakeDuePatterFactForOnAirRender, full stop, so there is no fact here to pass even by
            // mistake; see that method's own remarks for why a preview must never be ABLE to consume
            // the break's one due fact, not merely configured not to.
            var raw = await RequestCompletionAsync(
                cfg, request, personaOverride, card: null, updateTasteMemory: false, patterFact: null,
                queueWaitBudget: TimeSpan.FromSeconds(cfg.PreviewQueueWaitSeconds), ct);
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
        catch (LlmGateBusyException)
        {
            // Not a failure either — the gate is held by an on-air render and nothing was
            // attempted, so no WARN (F69.7's contract covers failed attempts). One INFO line so an
            // operator-facing 503 stays correlatable with server logs.
            logger.LogInformation(
                "Persona preview declined: LLM busy with another render (gave up after {WaitSeconds}s queue wait)",
                cfg.PreviewQueueWaitSeconds);
            return new PersonaPreviewResult.Busy();
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

    /// <summary>
    /// SPEC F107.5 (STORY-298, PLAN T225) — the patter lane's ONE pull point in this writer: called
    /// exclusively from <see cref="WriteAsync"/>, and only for the two music-adjacent kinds a patter
    /// fact is meant to season (<see cref="LlmPromptBuilder.IsPatterFactKind"/> — the single source
    /// of truth this method shares with <see cref="LlmPromptBuilder.BuildUserContent"/>'s own
    /// defense-in-depth kind re-check, review finding PLAN T225, so the two can never drift apart).
    /// Deliberately excludes <see cref="SegmentKind.SignOff"/>/<see cref="SegmentKind.SignOn"/>
    /// (handoff ceremonies, not "stay cool cats" track patter — a fact riding a sign-off would read
    /// as a non sequitur) and <see cref="SegmentKind.ContextSegment"/> itself (that segment IS a
    /// provider's facts already; layering a second, unrelated fact from a DIFFERENT provider on top
    /// would be a confusing double-fact break, not an enrichment).
    ///
    /// <see cref="IContextPatterFactSource.TryTakeDuePatterFact"/> is a CONSUMING read (see that
    /// interface's own remarks) — this method, and this method alone, ever calls it, and it is
    /// called from nowhere but <see cref="WriteAsync"/>. <see cref="WritePreviewAsync"/> never calls
    /// this method at all — not "calls it and discards the result", which would still burn the slot
    /// — which is what keeps auditioning a persona in the admin UI from ever being able to eat the
    /// current break's only due fact out from under the on-air render that actually airs (the exact
    /// CQS trap the T222 review flagged for <c>GenWave.Context.ContextPipeline</c>'s own TryTake).
    /// </summary>
    ContextPatterFact? TakeDuePatterFactForOnAirRender(SegmentKind kind) =>
        LlmPromptBuilder.IsPatterFactKind(kind)
            ? (patterFactSource ?? NoOpContextPatterFactSource.Instance).TryTakeDuePatterFact()
            : null;

    async Task<string> RequestCompletionAsync(
        LlmOptions cfg, SegmentRequest request, Persona? persona, PersonaCard? card,
        bool updateTasteMemory, ContextPatterFact? patterFact, TimeSpan? queueWaitBudget, CancellationToken ct)
    {
        // Captured up front, once, for LlmCallRing (SPEC F73.1, T41) — startedAt mirrors
        // LlmCopyStatusHolder's own attemptedAt semantics (includes any single-flight queueing wait
        // below), and mode is read fresh right here rather than threaded in as a parameter: a
        // preview call never passes through DegradationGatedCopyWriter (SPEC F69.4), so there is no
        // caller-evaluated mode available for that path, and reading IDegradationModeReader
        // uniformly for every path keeps this the one recording point instead of two.
        var startedAt = timeProvider.GetUtcNow();
        var mode = degradationMode.CurrentMode;
        // gh-#429: the SAME card-first-then-legacy-row precedence the prompt's own self-name-mention
        // line already uses (LlmPromptBuilder.BuildSelfNameMentionLine) — resolved once, up front, so
        // the ring entry names whichever persona authored this call whether it succeeds or faults.
        var personaName = LlmPromptBuilder.ResolveName(persona, card);

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
        //
        // A non-null queueWaitBudget (preview path only) bounds that wait: an operator staring at
        // a spinner must not queue behind a render-ahead burst, so a miss throws LlmGateBusyException
        // — still before the gate is held and before anything is attempted, so it skips the ring
        // and every catch below by the same nothing-was-attempted logic as a queued cancel.
        if (queueWaitBudget is { } budget)
        {
            if (!await singleFlight.WaitAsync(budget, ct))
                throw new LlmGateBusyException();
        }
        else
        {
            await singleFlight.WaitAsync(ct);
        }
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

            var http = httpClientFactory.CreateClient(HttpClientName);

            // Built before the request goes out (moved ahead of EndpointUri.Combine, T41 review
            // finding) so systemPrompt/userPrompt are available to the ring for every failure this
            // method can raise, not just the ones after prompt assembly.
            //
            // gh-#150: real DJs occasionally say their own name. The roll is taken HERE, outside
            // the pure builder (the same posture BuildStationClockLine takes with StationLocalNow —
            // nondeterminism stays injectable at the seam, the builder stays a pure function), from
            // Random.Shared — the source the builder's own SampleQuirks and Orchestration's
            // SystemRandomSource already standardize on. The builder enforces the persona gate
            // itself: with no persona section there is no line, however the roll lands.
            var mentionOwnName = Random.Shared.NextDouble() < LlmPromptBuilder.SelfNameMentionProbability;
            systemPrompt = LlmPromptBuilder.BuildSystemPrompt(
                LlmPromptBuilder.BuildPersonaSection(persona, card, mentionOwnName));

            // Read HERE — after WaitAsync above, i.e. already inside the single-flight critical
            // section — not by the caller before this method was ever invoked (SPEC F83.1, T65
            // review finding): Orchestrator.EnqueuePatterAsync starts a unit's BackAnnounce and
            // LeadIn renders concurrently, so reading the field any earlier could race the second
            // render's snapshot against the first render's own write. Only an on-air call
            // (updateTasteMemory) reads the real field; a preview always compares against empty.
            IReadOnlyList<string> previouslyVoicedTasteNotes = updateTasteMemory ? previousBreakTasteNotes : [];
            // patterFact?.Fact (SPEC F107.5, PLAN T225): already TAKEN by the caller (WriteAsync, via
            // TakeDuePatterFactForOnAirRender) — this method only renders what it was handed, it
            // never calls IContextPatterFactSource itself. Null for every WritePreviewAsync call.
            userPrompt = LlmPromptBuilder.BuildUserContent(
                request, LlmPromptBuilder.BuildStationClockLine(StationLocalNow()), previouslyVoicedTasteNotes,
                patterFact?.Fact);

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

            // Written HERE, STILL inside the single-flight critical section (SPEC F83.1, T65 review
            // finding) — only for an on-air call (updateTasteMemory), and only once the call has
            // actually succeeded; an exception anywhere above skips straight to the catch-all below,
            // leaving the field untouched for whichever break renders next. Doing the write before
            // singleFlight.Release() (in the finally below) is what guarantees the second render of
            // a concurrent BackAnnounce/LeadIn pair always sees THIS break's fresh notes rather than
            // a snapshot taken before this call ever ran.
            if (updateTasteMemory)
                previousBreakTasteNotes = LlmPromptBuilder.DescribeFiredRules(request.Track?.PersonaPick?.FiredRules ?? []);

            // Ok records the RAW reply (SPEC F73.1) — a later CleanCopy rejection (empty/over-length)
            // is a hygiene decision the caller makes, not a fact about whether the call itself
            // succeeded; see LlmCallOutcome.Ok's own remarks.
            callRing.Record(
                personaName, systemPrompt, userPrompt, text, startedAt, ElapsedMs(startedAt),
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
                personaName, systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode);
            throw;
        }
        finally
        {
            singleFlight.Release();
        }
    }

    long ElapsedMs(DateTimeOffset startedAt) => (long)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;

    // gh-#117 — the ONE place this writer resolves "station-local now" for the prompt's clock
    // line: Station:Timezone via the live IStationClockProvider seam when the composition supplies
    // one, otherwise the container's own clock (timeProvider.LocalTimeZone) — byte-identical to
    // the pre-gh-#117 behavior for every rig that never registers the seam.
    DateTimeOffset StationLocalNow() =>
        stationClock?.LocalNow ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone);

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
        var text = StripChatPreamble(raw.Trim());   // gh-#186 — must run BEFORE quote unwrapping
        text = StripWrappingQuotes(text);
        text = NewlinePattern.Replace(text, " ");
        text = BracketStageDirectionPattern.Replace(text, string.Empty);
        text = AsteriskStageDirectionPattern.Replace(text, string.Empty);
        text = MarkdownEmphasisPattern.Replace(text, string.Empty);
        text = RepeatedWhitespacePattern.Replace(text, " ").Trim();

        if (text.Length == 0)
            return null;

        return text.Length > maxChars ? null : text;
    }

    /// <summary>
    /// gh-#186: drops a chat preamble in front of the copy ("Here's your lead-in copy:" followed
    /// by the quoted body) — observed live rendering the preamble to air, because
    /// <see cref="StripWrappingQuotes"/> only fires when the text STARTS with a quote. Three
    /// conditions must all hold before anything is dropped, so legitimate announcer copy that
    /// merely contains a colon survives: the first colon comes early (≤80 chars) with no quote or
    /// line break before it; the preamble contains a meta word (<see cref="PreambleMetaWordPattern"/> —
    /// a model talking ABOUT the copy, e.g. "Up next:" has none and is kept); and everything after
    /// the colon is one quoted block running to the very end (a mid-copy quotation like
    /// <c>Up next: "Blue Monday" by New Order</c> has trailing text and is kept). Returns the
    /// quoted body still wrapped — <see cref="StripWrappingQuotes"/> unwraps it next.
    /// </summary>
    static string StripChatPreamble(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon > 80)
            return text;

        var preamble = text[..colon];
        if (preamble.AsSpan().IndexOfAny('"', '“', '\n') >= 0)
            return text;
        if (!PreambleMetaWordPattern.IsMatch(preamble))
            return text;

        var body = text[(colon + 1)..].Trim();
        if (body.Length < 2)
            return text;

        var opensQuoted = body[0] is '"' or '“';
        var closesQuoted = body[^1] is '"' or '”';
        return opensQuoted && closesQuoted ? body : text;
    }

    static string StripWrappingQuotes(string text)
    {
        if (text.Length >= 2)
        {
            var first = text[0];
            var last = text[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\'')
                || (first == '“' && last == '”'))
                return text[1..^1].Trim();
        }

        return text;
    }
}
