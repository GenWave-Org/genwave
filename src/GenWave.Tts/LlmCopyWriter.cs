namespace GenWave.Tts;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Llm;

/// <summary>
/// LLM-backed <see cref="ISegmentCopyWriter"/> (SPEC F34.2-F34.5, F92.2, F92.5, F107.3): authors
/// copy for exactly the kinds <see cref="IsLlmAuthored"/> reports true for — <see cref="SegmentKind.LeadIn"/>,
/// <see cref="SegmentKind.BackAnnounce"/>, <see cref="SegmentKind.SignOff"/>,
/// <see cref="SegmentKind.SignOn"/>, and, as of T224, <see cref="SegmentKind.ContextSegment"/> —
/// from an OpenAI-compatible chat-completions endpoint. <see cref="SegmentKind.StationId"/>,
/// <see cref="SegmentKind.TimeDate"/>, and <see cref="SegmentKind.Crosstalk"/> always delegate
/// straight to <paramref name="fallback"/> with zero HTTP — brand/time copy stays fixed and
/// forever-cached, and Crosstalk's real copy arrives via its own ahead-of-air script writer
/// (T282, SPEC F127.3) rather than this seam.
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
/// (<see cref="WritePreviewAsync"/>) alike — funnels through the single seam
/// <see cref="RequestCleanedCompletionAsync"/>: not a bare HTTP request, but the request PLUS the
/// F123.2-F123.4 hygiene/salvage pass (<see cref="CleanCopy"/>) PLUS the one <see cref="LlmCallRing"/>
/// recording point, all under the same single-flight gate (SPEC F69.6, gh-#36) — two concurrent
/// renders on the same backend double each other's latency, so this writer (a DI singleton, see
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
/// Both the read and the write live INSIDE <see cref="RequestCleanedCompletionAsync"/>'s own single-flight
/// critical section, not around it (T65 review finding): <c>Orchestrator.EnqueuePatterAsync</c>
/// starts one unit's BackAnnounce and LeadIn renders concurrently — both TTS renders are kicked off
/// before either is awaited — so a read/write pair taken OUTSIDE the gate could have the second
/// render capture the snapshot from before the first render's own write ever lands, and the two
/// would never compare against each other. Doing both only once <see cref="RequestCleanedCompletionAsync"/>
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
///
/// <para>
/// The show-flavor line SHARES that same slot (SPEC F116.3, STORY-308, PLAN T249, amending F107.5):
/// <see cref="TakeDueShowFlavorLineForOnAirRender"/> is only ever consulted when
/// <see cref="TakeDuePatterFactForOnAirRender"/> answered <see langword="null"/> for this break —
/// "context wins" — so a due show line that loses the slot to a context fact is never even ASKED for,
/// which is what keeps its own show's cadence window from being spent on a line that never aired. See
/// that method's own remarks for the full reasoning.
/// </para>
///
/// <para>
/// Crosstalk supersedes BOTH lanes at once (SPEC F127.9, STORY-329, PLAN T287): a break vending a
/// <see cref="SegmentKind.Crosstalk"/> exchange stamps <see cref="SegmentRequest.CrosstalkAiredThisBreak"/>
/// true on that SAME break's LeadIn/BackAnnounce request — one voice-moment per break — and both
/// <see cref="TakeDuePatterFactForOnAirRender"/> and <see cref="TakeDueShowFlavorLineForOnAirRender"/>
/// gate on it, never even asking either seam (the identical never-even-ask discipline the show-flavor
/// line's own "context wins" paragraph above already establishes).
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
    LlmCallRecorder recorder,
    IDegradationModeReader degradationMode,
    IStationClockProvider? stationClock = null,
    IContextPatterFactSource? patterFactSource = null,
    IShowFlavorLineSource? showFlavorLineSource = null)
    : ISegmentCopyWriter, IPersonaPreviewWriter, IAnnouncementCopyWriter
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
    /// Chars-per-token divisor used to derive the completion request's <c>max_tokens</c> cap from
    /// <see cref="LlmOptions.MaxCopyChars"/> (SPEC F123.1, STORY-319, PLAN T262) — one knob, not
    /// two: an operator only ever sets <c>Llm:MaxCopyChars</c>, and this cap is recomputed from it
    /// fresh on every call rather than read from a second setting. English averages roughly 4
    /// chars/token, but dividing by fewer chars/token here is DELIBERATE headroom: the generation
    /// cap must never be the reason a sentence that would have fit under MaxCopyChars gets cut off
    /// by the model itself mid-thought — that would leave T263's sentence-trim salvage nothing
    /// complete to cut at. A smaller divisor over-estimates the token budget, so the cap always
    /// lands comfortably above what MaxCopyChars alone allows.
    /// </summary>
    const int CharsPerTokenDivisor = 3;

    /// <summary>
    /// Floor for the derived <c>max_tokens</c> cap (SPEC F123.1, STORY-319, PLAN T262): guards a
    /// degenerate tiny <c>Llm:MaxCopyChars</c> (an operator typo, or the option's own [Range(1, ..)]
    /// minimum) from deriving a cap of zero or a handful of tokens — some OpenAI-compatible
    /// backends reject a near-zero <c>max_tokens</c> outright rather than degrading to a short
    /// reply, which would poison every completion call instead of merely capping it short. 16
    /// tokens is comfortably enough for a single short clause while staying tiny in absolute terms.
    /// </summary>
    const int MinGenerationTokenCap = 16;

    /// <summary>
    /// Ceiling for the derived <c>max_tokens</c> cap (SPEC F123.1, STORY-319, PLAN T262, review
    /// finding): the OTHER end of the same poison-every-call risk the floor guards against.
    /// <see cref="LlmOptions.MaxCopyChars"/> is <c>[Range(1, int.MaxValue)]</c> at the options
    /// layer — an env-set <c>int.MaxValue</c> would otherwise derive a nonsense <c>max_tokens</c>
    /// in the hundreds of millions. The admin settings surface (<c>SettingValidator.MaxCopyCharsMax</c>)
    /// caps an operator's live edit at 10000 chars, which this same formula would derive to ~3333
    /// tokens — 4096 is a conventional completion-length ceiling that sits comfortably above that
    /// surface's own maximum while still bounding the raw options layer.
    /// </summary>
    const int MaxGenerationTokenCap = 4096;

    /// <summary>
    /// Serializes every backend completion call (SPEC F69.6, gh-#36) — concurrent CPU generations on
    /// the same backend double each other's latency, so at most one <see cref="RequestCleanedCompletionAsync"/>
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
    /// <see cref="RequestCleanedCompletionAsync"/>'s own single-flight critical section (T65 review
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

    // SPEC F123.2 (STORY-319, PLAN T263) — a sentence boundary, kept deliberately simple: a
    // terminator, one optional closing quote mark, then whitespace or end-of-text. The regex is
    // ONLY the candidate finder; whether a `.` candidate is actually a sentence's end (and not an
    // abbreviation's period) is decided per-candidate by IsAbbreviationBoundary below — see that
    // method's own remarks (F123.2 review finding, gh-#277 follow-up: "from St." airing for St.
    // Vincent) for why that check lives in code rather than growing this pattern.
    static readonly Regex SentenceBoundaryPattern = new(@"[.!?][""'”’]?(?=\s|$)", RegexOptions.Compiled);

    // Abbreviations whose trailing period is never a sentence's end (SPEC F123.2 review finding):
    // "St. Vincent", "Dr. Dre", "Mt. Joy" all read the period as mid-title, not a boundary to cut
    // at. Checked case-insensitively against the word immediately before the candidate period —
    // see IsAbbreviationBoundary. Deliberately a short, curated list (not a general abbreviation
    // dictionary): each entry is a real title-adjacent abbreviation this station's copy has
    // actually produced, and the bias throughout this salvage is toward OVER-rejecting a boundary
    // (a shorter blurb beats airing a chopped abbreviation) rather than exhaustively cataloging
    // every abbreviation in English.
    static readonly HashSet<string> KnownAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dr", "Mr", "Mrs", "Ms", "St", "Mt", "Sgt", "Jr", "Sr", "Ft", "Bros", "vs", "etc", "approx", "feat",
    };

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
    /// relies on staying in sync with (see that method's remarks): <see cref="SegmentKind.StationId"/>,
    /// <see cref="SegmentKind.TimeDate"/>, and (as of PLAN T281) <see cref="SegmentKind.Crosstalk"/>
    /// are the three kinds this reports false for today, and none of them ever reach a prompt.
    ///
    /// <see cref="SegmentKind.Announcement"/> (PLAN T342) is deliberately NOT a fourth true case here,
    /// even though its flavored half genuinely does call the LLM: <see cref="WriteAnnouncementAsync"/>
    /// is its own entry point with its own explicit <c>message</c> parameter, never routed through
    /// <see cref="WriteAsync"/>'s <see cref="SegmentRequest"/>-only signature at all — a
    /// <see cref="SegmentRequest"/> cannot carry the owner's message text (see that method's own
    /// remarks) — so this gate and <see cref="LlmPromptBuilder.BuildSegmentLine"/>'s switch never need
    /// to know Announcement exists.
    /// </summary>
    static bool IsLlmAuthored(SegmentKind kind) =>
        kind is SegmentKind.LeadIn or SegmentKind.BackAnnounce or SegmentKind.SignOff or SegmentKind.SignOn
            or SegmentKind.ContextSegment;

    public async Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct)
    {
        // StationId/TimeDate stay templated — brand/time copy must be crisp, consistent, and
        // forever-cacheable. Crosstalk (PLAN T281) also stays templated here, for a different
        // reason: its real copy arrives via its own ahead-of-air script writer (T282, SPEC F127.3),
        // not this seam, so this writer is template-class for it by design, not degrading. The two
        // track-anchored kinds (F34.2) and the two handoff kinds (F92.2, F92.5) are the ones worth an
        // LLM's while (see IsLlmAuthored, the single source of truth).
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
            // RequestCleanedCompletionAsync below (which WritePreviewAsync also calls).
            //
            // SPEC F127.9 (STORY-329, PLAN T287) — "banter supersedes": request.CrosstalkAiredThisBreak
            // gates BOTH takes below, exactly like the request.Kind gate every other caller already
            // reads (LlmPromptBuilder.IsPatterFactKind) — a break airing crosstalk never even ASKS
            // either seam for this render, the same "never even ask" CQS discipline
            // TakeDueShowFlavorLineForOnAirRender's own remarks describe one seam over, so a lost slot
            // costs neither lane its own cadence window.
            var patterFact = TakeDuePatterFactForOnAirRender(request.Kind, request.CrosstalkAiredThisBreak);
            // SPEC F116.3 (STORY-308, PLAN T249) — the show-flavor line's own pull point, ONLY
            // consulted when patterFact above is null; see TakeDueShowFlavorLineForOnAirRender's own
            // remarks for why "never even ask" (not "ask and discard") is what keeps a lost slot from
            // spending the show's own cadence window.
            var showFlavorFact = patterFact is null
                ? TakeDueShowFlavorLineForOnAirRender(request.Kind, request.CrosstalkAiredThisBreak)
                : null;
            // updateTasteMemory: true — this is an on-air call, so previousBreakTasteNotes is both
            // read and (on success) overwritten INSIDE RequestCleanedCompletionAsync's own single-flight
            // critical section (SPEC F83.1, T65 review finding); see that method's own remarks for
            // why the field can no longer be touched out here.
            var cleanup = await RequestCleanedCompletionAsync(
                cfg, request, persona, card, updateTasteMemory: true, patterFact, showFlavorFact,
                queueWaitBudget: null, announcementMessage: null, ct);
            var cleaned = TextOf(cleanup);
            if (cleaned is null)
            {
                statusHolder.Record(LlmAttemptOutcome.Failed, attemptedAt);
                // The reason NAMES the real cause (T331 review finding F3) — a truth-gate exhaustion
                // is not "empty or exceeded Llm:MaxCopyChars": that wrong-lever WARN sent an operator
                // at the endpoint/max-tokens settings for a failure those levers cannot fix. See
                // DescribeNullTextReason's own remarks.
                LogFailure(request, persona, cfg.Model, attemptedAt, exception: null,
                    reason: DescribeNullTextReason(cleanup, cfg.ReasoningEffort));
                return await fallback.WriteAsync(request, ct);
            }

            // A sentence-boundary salvage (SPEC F123.2-F123.4, STORY-319, PLAN T263) is a SUCCESS at
            // this coarse Ok/Failed grain — the copy airs either way. LlmCallRing's own
            // LlmCallOutcome.Trimmed (recorded inside RequestCleanedCompletionAsync above) is where the
            // salvage stays visible as its own outcome, for the /api/llm-calls debug lens alone; this
            // holder feeds GET /api/status and the F72 degradation walk-down, neither of which should
            // ever treat "aired, slightly shorter" as a failure.
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
    /// <see cref="RequestCleanedCompletionAsync"/> (identical prompt composition) and
    /// <see cref="CleanCopy"/> (identical hygiene) so the previewed text is provably what the
    /// on-air <see cref="WriteAsync"/> path would have produced for the same request/persona.
    /// Deliberate differences: NOTHING here degrades to <paramref name="fallback"/> on an LLM miss
    /// for LeadIn/BackAnnounce/SignOff/SignOn — that would misrepresent the persona being auditioned — this method
    /// never records to <see cref="LlmCopyStatusHolder"/> (that holder tracks on-air attempts for
    /// <c>GET /api/status</c>; preview activity never airs and must not appear there), and it always
    /// passes <c>updateTasteMemory: false</c> to <see cref="RequestCleanedCompletionAsync"/> (SPEC F83.1,
    /// T65) — auditioning a card is not a real on-air break, so it neither reads nor perturbs
    /// <see cref="previousBreakTasteNotes"/>.
    /// </summary>
    public async Task<PersonaPreviewResult> WritePreviewAsync(
        SegmentRequest request, Persona? personaOverride, CancellationToken ct)
    {
        // StationId/TimeDate/Crosstalk route straight to the template rung — mirrors WriteAsync's
        // own kind-based routing (F34.2, IsLlmAuthored; see that method's own comment for why
        // Crosstalk joins the two — its real copy arrives via its own ahead-of-air script writer,
        // T282, SPEC F127.3, not this seam). This is not a fallback: none of the three call the LLM
        // on-air either, so template text IS the correct preview for them.
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
            // patterFact/showFlavorFact: null, ALWAYS (SPEC F107.5/F116.3, PLAN T225/T249) — this
            // method never calls TakeDuePatterFactForOnAirRender or TakeDueShowFlavorLineForOnAirRender,
            // full stop, so there is nothing here to pass even by mistake; see those methods' own
            // remarks for why a preview must never be ABLE to consume either break-scoped slot, not
            // merely configured not to.
            var cleanup = await RequestCleanedCompletionAsync(
                cfg, request, personaOverride, card: null, updateTasteMemory: false, patterFact: null,
                showFlavorFact: null, queueWaitBudget: TimeSpan.FromSeconds(cfg.PreviewQueueWaitSeconds),
                announcementMessage: null, ct);
            var cleaned = TextOf(cleanup);
            // DescribeNullTextReason (review round-2 finding F2, PLAN T332): the SAME reason
            // WriteAsync's own failure WARN already names, reused here rather than a second, hardcoded
            // "empty or over-length" message — a truth-gate reject reaching this branch (a preview
            // exhausting the F138.4 ladder, now reachable since T332 widened the ladder to every kind)
            // deserves the honest cause too, not the wrong-lever hygiene wording that sends an operator
            // at settings a truth-gate failure has nothing to do with. Safe to surface on this
            // authenticated admin surface: every interpolated fragment is either a fixed phrase or a
            // ClaimViolation.Token, which is provably digit-shaped or closed-vocabulary (that type's
            // own remarks), never free text.
            return cleaned is null
                ? new PersonaPreviewResult.Failed($"The LLM reply was rejected ({DescribeNullTextReason(cleanup, cfg.ReasoningEffort)}).")
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
            LogFailure(
                request, personaOverride, cfg.Model, attemptedAt, ex, reason: null,
                outcomeKind: LogFailureOutcome.ReportingToPreviewCaller);
            return new PersonaPreviewResult.Failed("The LLM request failed. Check the server logs for details.");
        }
    }

    /// <summary>
    /// <see cref="IAnnouncementCopyWriter"/> (SPEC F144.3/F144.4, STORY-358, PLAN T342) — the flavored
    /// half of an owner announcement's on-air delivery: the message is injected into a fresh
    /// completion as an OWNER-TRUSTED fact (mirrors <see cref="SegmentKind.ContextSegment"/>'s own
    /// fact-block trust, F138.2's "supported by the fact block" contract, now scoped to the owner's
    /// own words), riding the SAME <see cref="RequestCleanedCompletionAsync"/> machinery (single-flight
    /// gate, hygiene, the F138.4 re-ask ladder, F139 cause stamping) every other LLM-authored kind
    /// already shares — plus one ADDITIONAL check no other kind carries: the case-folded message CORE
    /// must survive in the rendered copy (SPEC F144.3, the F68.4 case-folded-survival precedent),
    /// checked and re-asked exactly like any other truth-gate violation (see
    /// <see cref="CheckTruthGate"/>'s own <c>requiredCore</c> parameter).
    ///
    /// <para>
    /// <b>Why NOT <see cref="WriteAsync"/>'s own <see cref="SegmentRequest.Kind"/> routing:</b>
    /// <see cref="SegmentRequest"/> is a published Abstractions record that cannot gain a "message
    /// text" member (F144.1's own carry-forward), so <paramref name="message"/> arrives as its OWN
    /// explicit parameter — this method's signature, never <see cref="SegmentRequest"/>'s. That is
    /// exactly why <see cref="IsLlmAuthored"/> and <see cref="LlmPromptBuilder.BuildSegmentLine"/> gain
    /// no <see cref="SegmentKind.Announcement"/> arm: both exist to route
    /// <see cref="SegmentRequest"/>-ONLY calls (<see cref="WriteAsync"/>, <see cref="WritePreviewAsync"/>)
    /// that carry no message to flavor in the first place — an Announcement-kind preview would
    /// otherwise build a facts-free, message-free prompt with nothing to say. This method instead
    /// composes its own prompt via <see cref="LlmPromptBuilder.BuildAnnouncementUserContent"/>, called
    /// nowhere else.
    /// </para>
    ///
    /// <para>
    /// <b>THE FALLBACK LAW (SPEC F144.4) lives here as ONE early return plus the existing ladder's
    /// existing null floor</b> — this method never throws (except the caller's own cancellation,
    /// propagated) and never returns partial/violating copy: a disabled writer, an unreachable
    /// endpoint, a blown render budget, or an exhausted ladder (containment or fact/clock violation)
    /// all resolve to <see langword="null"/> — the caller's own signal (the Orchestrator's announcement
    /// vend step) to render <paramref name="message"/> itself, verbatim, through the SAME
    /// <c>IVerbatimSegmentRenderer</c> F144.2 already uses. The caller's whole fallback is one
    /// <c>??</c>.
    /// </para>
    ///
    /// <para>
    /// Deliberately does NOT touch <see cref="statusHolder"/> (SPEC F34.8) — that holder's
    /// <see cref="LlmCopyStatusHolder.ConsecutiveFailureCount"/> drives
    /// <see cref="IDegradationModeReader"/>'s STATION-WIDE auto-drop signal for every ordinary patter
    /// kind (LeadIn/BackAnnounce/SignOff/SignOn/ContextSegment, SPEC F69.2); an announcement's own
    /// containment reject is a fact about THIS message, not the model's general health, and must never
    /// itself trip the whole station into Soft/Hard degradation the way a run of genuine
    /// LeadIn/BackAnnounce misses would. <see cref="LlmCallRing"/>/<see cref="LlmCallCauseCounters"/>
    /// (via <see cref="recorder"/>, stamped <see cref="LlmCallKind.Announcement"/>) still see every
    /// attempt — the F139 bench/cause surface must, per SPEC F139.1's own reach, even though the
    /// degradation signal must not.
    /// </para>
    ///
    /// <para>
    /// <b>THE HARD-MODE RULING (MEDIUM-4 review finding, PLAN T342 round 2):</b>
    /// <see cref="TtsServiceCollectionExtensions"/> registers <see cref="IAnnouncementCopyWriter"/>
    /// straight against this singleton, never through <see cref="DegradationGatedCopyWriter"/> — that
    /// class's own <see cref="ISegmentCopyWriter"/> contract has no shape for "return null instead of
    /// template text", so it cannot wrap this seam without bending a contract it was never asked to
    /// bend. Left unguarded, a Hard-degraded station would still burn 1-2 real completion calls per
    /// flavored announcement AND hold the shared <see cref="singleFlight"/> gate the feeder path
    /// needs, in direct tension with <see cref="DegradationMode.Hard"/>'s own "zero LLM calls" contract
    /// (SPEC F69.1) that <see cref="DegradationGatedCopyWriter"/> already enforces for every ordinary
    /// LeadIn/BackAnnounce/SignOff/SignOn/ContextSegment render. RULED: in
    /// <see cref="DegradationMode.Hard"/>, this method skips the flavor attempt entirely and takes the
    /// F144.4 verbatim floor immediately — the owner's own message still airs, exactly as it would
    /// after an exhausted ladder; nothing is lost but the DJ's in-character framing. Implemented the
    /// lightest lawful way: this method already takes <see cref="degradationMode"/>
    /// (<see cref="IDegradationModeReader"/>) for its own <see cref="LlmCallRing"/> mode stamp — the
    /// SAME read seam <see cref="DegradationGatedCopyWriter"/> itself reads through
    /// <see cref="DegradationController"/> — so consulting it here, before any HTTP call or gate
    /// acquisition, needs no new interface, no wrapper, and no change to
    /// <see cref="DegradationGatedCopyWriter"/>'s own contract at all. Deliberately silent (no WARN, no
    /// <see cref="LlmCallRing"/> entry) — this is a planned skip, not a failure to diagnose, mirroring
    /// <see cref="DegradationGatedCopyWriter.WriteAsync"/>'s own silent Hard-mode routing one seam
    /// over. Soft and Normal are UNCHANGED by this ruling — both still attempt the flavor render
    /// exactly as before (Soft's own "minimized calls" cadence lives entirely inside
    /// <see cref="DegradationGatedCopyWriter"/>'s ordinary <see cref="ISegmentCopyWriter"/> path, never
    /// this one; nothing about that cadence applies to an announcement, which is not a cadence-gated
    /// kind — SPEC F144.1's own "cadence-independent" ruling one layer up, at the Orchestrator seam).
    /// </para>
    /// </summary>
    public async Task<string?> WriteAnnouncementAsync(SegmentRequest request, string message, CancellationToken ct)
    {
        // MEDIUM-4 ruling (see this method's own remarks above): Hard degradation takes the verbatim
        // floor immediately, with ZERO LLM calls and no singleFlight hold — checked before anything
        // else in this method, so a Hard-degraded station never dispatches a request, never queues
        // behind the feeder path's own gate, and never records a ring entry for a call that was never
        // attempted.
        if (degradationMode.CurrentMode == DegradationMode.Hard)
            return null;

        var attemptedAt = DateTimeOffset.UtcNow;
        LlmOptions? cfg = null;
        Persona? persona = null;
        try
        {
            cfg = optionsMonitor.CurrentValue;
            if (string.IsNullOrEmpty(cfg.Endpoint))
                return null;

            // Same F35.2/F35.3 posture as WriteAsync — the ACTIVE persona voices the announcement, so
            // it airs "in character" as F144.3 requires, never a personality-neutral read.
            persona = await personaAccessor.ResolveAsync(ct);
            var card = await personaAccessor.ResolveCardAsync(ct);

            var cleanup = await RequestCleanedCompletionAsync(
                cfg, request, persona, card, updateTasteMemory: false, patterFact: null, showFlavorFact: null,
                queueWaitBudget: null, announcementMessage: message, ct);
            var cleaned = TextOf(cleanup);
            if (cleaned is null)
            {
                // The reason names the real cause (T331 review finding F3, extended to the F144.3
                // containment floor by DescribeNullTextReason's own AnnouncementCore arm) — never the
                // wrong-lever "empty or exceeded Llm:MaxCopyChars" wording (carry-forward #6).
                LogFailure(
                    request, persona, cfg.Model, attemptedAt, exception: null,
                    reason: DescribeNullTextReason(cleanup, cfg.ReasoningEffort), outcomeKind: LogFailureOutcome.FallingBackToVerbatimRead);
            }

            return cleaned;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled — not our own Llm:TimeoutSeconds budget expiring. Propagate; this
            // is not a flavoring failure for the caller's fallback law to act on.
            throw;
        }
        catch (Exception ex)
        {
            LogFailure(
                request, persona, cfg?.Model, attemptedAt, ex, reason: null,
                outcomeKind: LogFailureOutcome.FallingBackToVerbatimRead);
            return null;
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
    /// at WARN. <paramref name="outcomeKind"/> (PLAN T342, widened from a bool) is the ONE varying
    /// clause across this writer's three failure surfaces — see <see cref="LogFailureOutcome"/>'s own
    /// remarks for what each value says happens next.
    /// </summary>
    void LogFailure(
        SegmentRequest request, Persona? persona, string? model, DateTimeOffset attemptedAt,
        Exception? exception, string? reason, LogFailureOutcome outcomeKind = LogFailureOutcome.FallingBackToTemplate)
    {
        var detail = exception switch
        {
            HttpRequestException { StatusCode: { } status } => $"HTTP {(int)status}",
            { } ex => ex.GetType().Name,
            null => reason ?? "unknown failure",
        };
        var elapsedMs = (long)(DateTimeOffset.UtcNow - attemptedAt).TotalMilliseconds;
        var outcome = outcomeKind switch
        {
            LogFailureOutcome.ReportingToPreviewCaller => "reporting failure to the preview caller",
            LogFailureOutcome.FallingBackToVerbatimRead => "falling back to the verbatim read (SPEC F144.4)",
            _ => "falling back to template",
        };

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
    /// One INFORMATION line (SPEC F123.4) whenever a completion's own hygiene pass needed the
    /// sentence-boundary salvage — a trim is discipline, not an outage, so it gets its own quiet lane
    /// rather than promoting to <see cref="LogFailure"/>'s WARN. Shared by the first completion and
    /// the F138.4 re-ask alike (STORY-350, PLAN T331), so a re-ask reply that also needed salvaging
    /// is observable exactly the same way the first one always was — extracted here so the two call
    /// sites cannot drift onto two different messages for the same event.
    /// </summary>
    void LogIfTrimmed(SegmentRequest request, string? personaName, LlmCopyCleanupResult cleanup)
    {
        if (cleanup is not LlmCopyCleanupResult.Trimmed trimmed)
            return;

        logger.LogInformation(
            "LLM copy for {Kind} trimmed to the last complete sentence under Llm:MaxCopyChars " +
            "(persona: {PersonaName}): {CharsBefore} -> {CharsAfter} chars",
            request.Kind, (personaName ?? "none").ReplaceLineEndings(" "), trimmed.CharsBeforeTrim,
            trimmed.Text.Length);
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
    /// <param name="crosstalkAiredThisBreak">
    /// SPEC F127.9 (STORY-329, PLAN T287) — <see langword="true"/> when this SAME break is airing a
    /// <see cref="SegmentKind.Crosstalk"/> exchange (<see cref="SegmentRequest.CrosstalkAiredThisBreak"/>,
    /// stamped by <c>Orchestrator.EnqueuePatterAsync</c>'s own vend step, the ONE writer): the fact
    /// lane is never even asked for that break — one voice-moment per break, never a stacked "ask and
    /// discard" (the exact CQS trap this method's own remarks already describe for every other kind).
    /// </param>
    ContextPatterFact? TakeDuePatterFactForOnAirRender(SegmentKind kind, bool crosstalkAiredThisBreak) =>
        LlmPromptBuilder.IsPatterFactKind(kind) && !crosstalkAiredThisBreak
            ? (patterFactSource ?? NoOpContextPatterFactSource.Instance).TryTakeDuePatterFact()
            : null;

    /// <summary>
    /// SPEC F116.3 (STORY-308, PLAN T249) — the show-flavor line's own pull point, mirroring
    /// <see cref="TakeDuePatterFactForOnAirRender"/> exactly one seam over: called exclusively from
    /// <see cref="WriteAsync"/>, for the SAME two music-adjacent kinds
    /// (<see cref="LlmPromptBuilder.IsPatterFactKind"/> — shared, not duplicated, so the two seams can
    /// never drift on which kinds are eligible), and ONLY when <see cref="WriteAsync"/>'s own
    /// <c>patterFact</c> is null.
    ///
    /// <see cref="IShowFlavorLineSource.TryTakeDueShowLine"/> is a CONSUMING read (see that interface's
    /// own remarks) — this method, and this method alone, ever calls it, and it is called from nowhere
    /// but <see cref="WriteAsync"/>, and only on that null-patterFact branch (SPEC F116.3's own
    /// arbitration: "context wins... the show gate stays open for the next eligible break" — simply
    /// never calling this seam when a context fact already claimed the slot is what keeps a lost show
    /// line from spending its own cadence window; a "call and discard" shape would still burn it, the
    /// exact CQS trap <see cref="TakeDuePatterFactForOnAirRender"/>'s own remarks describe one seam
    /// over). <see cref="WritePreviewAsync"/> never calls this method at all, for the identical reason
    /// it never calls <see cref="TakeDuePatterFactForOnAirRender"/>.
    /// </summary>
    /// <param name="crosstalkAiredThisBreak">
    /// SPEC F127.9 — same gate, same reason, as <see cref="TakeDuePatterFactForOnAirRender"/>'s own
    /// identically-named parameter one seam over.
    /// </param>
    ShowFlavorFact? TakeDueShowFlavorLineForOnAirRender(SegmentKind kind, bool crosstalkAiredThisBreak) =>
        LlmPromptBuilder.IsPatterFactKind(kind) && !crosstalkAiredThisBreak
            ? (showFlavorLineSource ?? NoOpShowFlavorLineSource.Instance).TryTakeDueShowLine()
            : null;

    async Task<LlmCopyCleanupResult> RequestCleanedCompletionAsync(
        LlmOptions cfg, SegmentRequest request, Persona? persona, PersonaCard? card,
        bool updateTasteMemory, ContextPatterFact? patterFact, ShowFlavorFact? showFlavorFact,
        TimeSpan? queueWaitBudget, string? announcementMessage, CancellationToken ct)
    {
        // LOW-5 (PLAN T342 round 2, parameter-shape finding): announcementMessage carries FOUR
        // distinct meanings through the rest of this method — which LlmCallKind to stamp, which
        // prompt builder to call, what factBlock the truth gate's CheckFacts/CheckClock halves see,
        // and what requiredCore CheckContainment/CheckClock's own owner-trust exemption enforce — each
        // previously re-derived independently from announcementMessage's own nullability at its own
        // scattered call site further down this method (a connascence-of-meaning smell: four places
        // that all had to independently agree announcementMessage's null-ness means "this is an
        // announcement call", never provably in sync). callKind and factBlock need nothing this
        // method computes LATER (both depend only on announcementMessage/request, already in scope),
        // so both resolve fully right here; requiredCore is announcementMessage itself, given its own
        // name so every later reference — the prompt-selector below, and CheckTruthGate's own
        // parameter one seam over — reads this ONE local rather than announcementMessage's own
        // nullability a second, third, or fourth time. Stamped onto every recorder.Record call below
        // (SPEC F139.1) so a truth-gate reject on THIS lane is visible on the F139 bench/cause surface
        // as its own kind, never folded into ordinary Copy noise (this task's own carry-forward).
        var callKind = announcementMessage is not null ? LlmCallKind.Announcement : LlmCallKind.Copy;
        var requiredCore = announcementMessage;
        // SPEC F144.3 (PLAN T342): announcementMessage, when present, IS the fact block — the
        // owner's own words are the mechanical "owner-trusted fact" this gate must not reject as
        // fabrication (this task's own binding acceptance), the identical CheckFacts "supported by
        // the fact block" contract ContextSegment already relies on, now scoped to a message instead
        // of a provider's facts. Mutually exclusive with the ContextSegment branch below by
        // construction — announcementMessage is only ever non-null for an Announcement-kind request,
        // which is never a ContextSegment.
        var factBlock = requiredCore
            ?? (request.Kind == SegmentKind.ContextSegment
                && request.ContextFacts is { } contextFacts && !string.IsNullOrWhiteSpace(contextFacts)
                    ? contextFacts
                    : null);
        // Captured up front, once, for LlmCallRing (SPEC F73.1, T41) — startedAt mirrors
        // LlmCopyStatusHolder's own attemptedAt semantics (includes any single-flight queueing wait
        // below), and mode is read fresh right here rather than threaded in as a parameter: a
        // preview call never passes through DegradationGatedCopyWriter (SPEC F69.4), so there is no
        // caller-evaluated mode available for that path, and reading IDegradationModeReader
        // uniformly for every path keeps this the one recording point instead of two.
        //
        // startedAt is REASSIGNED, not read-only (T331 review finding F4b): it names WHICHEVER call
        // is currently in flight, mirroring userPrompt's own reassignment below — RunTruthGateLadderAsync
        // moves it to a re-ask's own dispatch instant the moment that call actually fires, so the
        // catch-all far below (and this render's OWN success recording, if the ladder never fires at
        // all) both always attribute a fault/success to the call that actually produced it, never to
        // an earlier call's timing.
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
            // Captured once (SPEC F138.5, PLAN T331): BuildSystemPrompt's own clock guard line and
            // BuildUserContent's station clock line below must provably read the SAME instant — the
            // identical discipline CopyClaims.CheckClock's own remarks already require of its caller.
            var stationLocalNow = StationLocalNow();
            systemPrompt = LlmPromptBuilder.BuildSystemPrompt(
                LlmPromptBuilder.BuildPersonaSection(persona, card, mentionOwnName), cfg.MaxCopyChars, stationLocalNow);

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
            // showFlavorFact (SPEC F116.3, PLAN T249): the same shape one seam over, already TAKEN by
            // WriteAsync via TakeDueShowFlavorLineForOnAirRender, and only ever non-null when
            // patterFact above was null (context wins) — BuildUserContent enforces that structurally
            // too (its own defense-in-depth `?? ` fallback), so this call passes both through as-is.
            //
            // SPEC F144.3 (PLAN T342): an announcement takes its OWN dedicated prompt builder —
            // BuildAnnouncementUserContent, never BuildUserContent's per-kind switch (see
            // WriteAnnouncementAsync's own remarks for why Announcement never joins that switch at
            // all). requiredCore (LOW-5: the same local callKind/factBlock above already derive from)
            // is non-null on exactly one caller (WriteAnnouncementAsync), so this branch can never
            // fire for an ordinary WriteAsync/WritePreviewAsync call.
            userPrompt = requiredCore is { } message
                ? LlmPromptBuilder.BuildAnnouncementUserContent(
                    request, LlmPromptBuilder.BuildStationClockLine(stationLocalNow), message)
                : LlmPromptBuilder.BuildUserContent(
                    request, LlmPromptBuilder.BuildStationClockLine(stationLocalNow), previouslyVoicedTasteNotes,
                    patterFact?.Fact, showFlavorFact);

            // No boot-frozen BaseAddress (F36.2) — the endpoint is read from CurrentValue above and an
            // absolute URI is built per call (EndpointUri preserves a subpath in Llm:Endpoint, e.g.
            // https://host/openai — a plain new Uri(base, "/v1/...") would drop it, T3 review finding),
            // so a live PUT to Llm:Endpoint applies on the next render.
            var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");

            var reply = await PostCompletionAsync(http, requestUri, cfg, systemPrompt, userPrompt, timeoutCts.Token);
            var text = reply.Content;

            // Written HERE, STILL inside the single-flight critical section (SPEC F83.1, T65 review
            // finding) — only for an on-air call (updateTasteMemory), and only once the call has
            // actually succeeded; an exception anywhere above skips straight to the catch-all below,
            // leaving the field untouched for whichever break renders next. Doing the write before
            // singleFlight.Release() (in the finally below) is what guarantees the second render of
            // a concurrent BackAnnounce/LeadIn pair always sees THIS break's fresh notes rather than
            // a snapshot taken before this call ever ran.
            if (updateTasteMemory)
                previousBreakTasteNotes = LlmPromptBuilder.DescribeFiredRules(request.Track?.PersonaPick?.FiredRules ?? []);

            // Hygiene + the F123.2 sentence-boundary salvage run HERE, inside the one ring-recording
            // point (SPEC F123.2-F123.4, STORY-319, PLAN T263), so a trim is visible to the ring as
            // its own outcome instead of only discoverable by re-reading Response after the fact.
            // gh-#620: a reasoning-only reply is recognised BEFORE hygiene ever sees the (empty) text,
            // so the fallback WARN can name the real cause; see CleanupOf's own remarks.
            var cleanup = CleanupOf(reply, cfg.MaxCopyChars);
            LogIfTrimmed(request, personaName, cleanup);

            // SPEC F138.2/F138.3/F138.4 (STORY-350, STORY-351, PLAN T331/T332) — the truth-gate
            // stage, covering every LLM-authored kind, not the context lane alone (as of PLAN T342,
            // also the announcement flavor lane below — a call this method itself, not IsLlmAuthored,
            // routes here). Composed as ONE
            // check (CheckTruthGate below), never two chained ladder runs — the T331 reviewer ruling
            // RunTruthGateLadderAsync's own remarks restate: a second ladder invocation could never
            // see the first's own re-ask reply, so a ContextSegment render facing BOTH a fact
            // violation and a clock violation must clear both in the SAME gate/re-ask cycle. factBlock
            // is non-null ONLY for a ContextSegment render carrying a non-empty fact block (F138.2's
            // own "never even ask" discipline for the FACTS half specifically — see below); the clock
            // half of CheckTruthGate always runs for every kind reaching this point, ContextSegment
            // included (F138.3's own "all patter kinds": LeadIn, BackAnnounce, and — since they
            // resolve through this exact same seam — SignOff/SignOn get it for free, no separate
            // wiring needed).
            //
            // No Kind-based whole-gate carve-out for a factless ContextSegment (review round-2
            // finding F1, PLAN T332 — an earlier revision of this method skipped the WHOLE gate,
            // clock half included, for that case; deleted): CheckTruthGate's own factBlock is-null
            // guard already keeps F138.2's "never even ask" scoped to the FACTS half alone, so
            // deleting the second, Kind-based gate here is strictly less code covering strictly more
            // spec, not a behavior loss. A factless ContextSegment is structurally unreachable on the
            // AIR path — Orchestrator.BuildContextSegmentRequestAsync's own blank-facts guard
            // (Orchestrator.cs, "no segment facts (SPEC F107.6)") never builds a ContextSegment
            // SegmentRequest without one — but IS reachable from GenWave.Host.Api.PersonaController.Preview
            // (TryParseKind accepts any SegmentKind name, and a preview never supplies ContextFacts at
            // all), so a factless ContextSegment preview now gets the clock half exactly like every
            // other kind, rather than silently skipping it. Also gated on TextOf(cleanup): hygiene
            // already rejecting the reply outright (empty, or over-length with nothing salvageable)
            // falls straight through to the unchanged switch/record below — there is no candidate text
            // to check claims against, and the existing OverLength/EmptyCompletion rung already covers
            // that case correctly. factBlock (LOW-5: hoisted to this method's own top, alongside
            // callKind/requiredCore) already folds in announcementMessage for exactly this reason —
            // see that derivation's own remarks.
            if (TextOf(cleanup) is { } candidate)
            {
                var ladderResult = await RunTruthGateLadderAsync(
                    candidateText => CheckTruthGate(
                        candidateText, factBlock, stationLocalNow, request.Track?.Title, requiredCore),
                    LlmPromptBuilder.BuildTruthGateReaskLine, candidate, text);
                if (ladderResult is not null)
                    return ladderResult;
            }

            // Ok still records the RAW reply (SPEC F73.1) regardless of outcome — a full reject
            // (empty/no-sentence-fits) stays Ok exactly as before T263 (a hygiene decision the caller
            // makes, not a fact about whether the call itself succeeded; see LlmCallOutcome.Ok's own
            // remarks); a trim gets its own finer-grained outcome instead (LlmCallOutcome.Trimmed).
            //
            // SPEC F139.1 (STORY-353, PLAN T330): the additive Cause field splits the SAME three
            // shapes finer still — a Trimmed salvage still aired, so it is Success exactly like an
            // exact Fits; a full Rejected splits on WHY nothing survived (LlmCopyCleanupResult.Rejected's
            // own WasOverLength, decided once at CleanCopy, never re-derived here).
            var (ringOutcome, cause) = ClassifyCleanup(cleanup);
            recorder.Record(
                personaName, systemPrompt, userPrompt, text, startedAt, ElapsedMs(startedAt),
                ringOutcome, statusDetail: null, mode, cause, cfg.Model, callKind);
            return cleanup;

            // SPEC F138.4 (STORY-350, PLAN T331) — one truth-gate ladder run: gate
            // firstCandidate against check, and on a violation, exactly ONE re-ask (its added
            // prompt line built by buildReaskLine), then reclassify. A LOCAL function (T331 review
            // finding — this method was pushing 190 lines with a SECOND ladder, PLAN T332's clock
            // check, landing at this exact seam next) — not a private instance method — because it
            // needs to REASSIGN userPrompt/startedAt and read http/requestUri/timeoutCts exactly the
            // way the enclosing method already does (review finding F4b: the SAME reassign-not-shadow
            // discipline userPrompt already followed for its own prompt text, now shared by startedAt
            // too, so a fault raised by the re-ask's own call is attributed — prompt AND timing alike
            // — to that call, never to the first). Returns null when check passed firstCandidate
            // outright — the caller's own signal to fall through to its unchanged, non-gated
            // classify/record path above; a non-null return is always this ladder's OWN final word,
            // and the caller records nothing further.
            //
            // PLAN T332 folds facts and clock into ONE call to this same ladder rather than a second
            // one (CheckTruthGate, the caller's own composite check, below) — never two chained
            // ladder runs: a second RunTruthGateLadderAsync invocation could never see the first's
            // own re-ask reply, so it could only ever check the ORIGINAL candidate a second time,
            // leaving whatever the first ladder's own re-ask actually said unchecked by the second
            // check entirely. Composing the two checks into one function, called once, is what keeps
            // this a single gate/re-ask/reclassify cycle regardless of how many claim classes it
            // covers.
            //
            // Plain // comments, not /// (T331 review finding): a local function's XML doc comment
            // never renders anywhere doc-gen actually reads it today, and would flag CS1587 the day
            // a future doc-gen pass enables XML output for this project.
            async Task<LlmCopyCleanupResult?> RunTruthGateLadderAsync(
                Func<string, ClaimCheckResult> check, Func<IReadOnlyList<ClaimViolation>, string> buildReaskLine,
                string firstCandidate, string firstRawText)
            {
                var gateResult = check(firstCandidate);
                if (gateResult.Passed)
                    return null;

                // The rejected first call gets its own honest ring entry (SPEC F138.4: each call in
                // the ladder is its own call with its own entry) BEFORE the re-ask fires — never
                // silently folded into whichever entry the re-ask itself produces.
                recorder.Record(
                    personaName, systemPrompt, userPrompt, firstRawText, startedAt, ElapsedMs(startedAt),
                    LlmCallOutcome.Ok, statusDetail: null, mode, LlmCallCause.TruthGateReject, cfg.Model, callKind);

                // Exactly ONE re-ask (F138.4), naming the violation. userPrompt AND startedAt are both
                // REASSIGNED (never shadowed by a new local) so a fault raised by THIS call — timeout,
                // non-2xx, connect — is attributed by this method's own catch-all below to the
                // re-ask's own prompt and its own start time, and degrades through the EXACT SAME path
                // an ordinary single-call failure already does: no new exception handling, no new
                // "longer hold". Reusing timeoutCts.Token (not a fresh CancelAfter) is what bounds the
                // whole ladder to this render's existing Llm:TimeoutSeconds budget — whatever the
                // first call already spent is gone, and a budget that expires mid-reask throws
                // OperationCanceledException exactly as it always would have for one call.
                userPrompt = $"{userPrompt}\n{buildReaskLine(gateResult.Violations)}";
                startedAt = timeProvider.GetUtcNow();
                var reaskReply = await PostCompletionAsync(http, requestUri, cfg, systemPrompt, userPrompt, timeoutCts.Token);
                var reaskText = reaskReply.Content;
                var reaskCleanup = CleanupOf(reaskReply, cfg.MaxCopyChars);
                LogIfTrimmed(request, personaName, reaskCleanup);

                // The tri-state (no candidate to even check / checked-and-failed / checked-and-passed)
                // folds to ONE clear bool (T331 review finding) instead of re-deriving
                // "is { Passed: false }" at both the classify site and the final-return site below:
                // reaskViolations is non-null exactly when the gate was actually checked AND failed.
                var reaskGateResult = TextOf(reaskCleanup) is { } reaskCandidate ? check(reaskCandidate) : null;
                var reaskViolations = reaskGateResult is { Passed: false } failed ? failed.Violations : null;

                var (reaskOutcome, reaskCause) = reaskViolations is null
                    ? ClassifyCleanup(reaskCleanup)
                    : (LlmCallOutcome.Ok, LlmCallCause.TruthGateReject);
                recorder.Record(
                    personaName, systemPrompt, userPrompt, reaskText, startedAt, ElapsedMs(startedAt),
                    reaskOutcome, statusDetail: null, mode, reaskCause, cfg.Model, callKind);

                // F138.4's floor: a still-violating re-ask never airs. A TruthGateRejected here is
                // what makes TextOf(...) null for it (T331 review finding F3 — its own distinct shape
                // from a hygiene Rejected, so WriteAsync's own failure WARN can name the real cause),
                // so the caller (WriteAsync/WritePreviewAsync) degrades it exactly like any other
                // reject, straight into fallback.WriteAsync — an EXISTING rung, never a new one
                // invented here. WHERE that rung actually lands differs by kind, unchanged by this
                // ladder (PLAN T332 investigation): ContextSegment/SignOff/SignOn never reach air even
                // as template copy — TtsSegmentSource's own non-fresh-copy guard drops the render
                // outright (F92.4/F92.5/F107.6's skip-never-silence posture); LeadIn/BackAnnounce carry
                // no such guard, so PatterTemplateRenderer's deterministic template airs instead — its
                // FIXED PROSE never states a weekday/daypart on its own (see
                // PatterTemplateRenderer.Expand's own LeadIn/BackAnnounce arms; it does interpolate the
                // track's own title/artist verbatim, and a title CAN legally name a day — "Saturday
                // Night Fever" — but that interpolated text is never gate-checked, since a template
                // render never passes through this ladder at all), the same F92-era floor a hygiene
                // reject already degraded those two kinds to before this gate ever existed. A re-ask
                // that passed hygiene but still failed the gate is the only case needing a synthetic
                // TruthGateRejected instead of reaskCleanup itself: reaskCleanup there still carries
                // real (unusable) text that must never reach TextOf.
                return reaskViolations is { } violations
                    ? new LlmCopyCleanupResult.TruthGateRejected(violations)
                    : reaskCleanup;
            }
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
            // userPrompt/startedAt name WHICHEVER call actually faulted (T331 review finding F4b): a
            // fault raised by RunTruthGateLadderAsync's own re-ask is attributed to the re-ask's own
            // prompt and dispatch instant, never the first call's, because both were REASSIGNED (not
            // shadowed) the moment that call fired — see this method's own startedAt remarks above.
            var (outcome, cause, detail) = ClassifyForRing(ex);
            recorder.Record(
                personaName, systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode, cause, cfg.Model, callKind);
            throw;
        }
        finally
        {
            singleFlight.Release();
        }
    }

    /// <summary>
    /// The composite truth-gate check <see cref="RunTruthGateLadderAsync"/> gates on (SPEC F138.2,
    /// F138.3, F144.3, STORY-350, STORY-351, PLAN T332, T342) — kept beside the ladder call site above,
    /// deliberately small and pure: a straight union of <see cref="CopyClaims.CheckFacts"/>'s
    /// violations (only when <paramref name="factBlock"/> is non-null — the context/announcement
    /// lanes' own fact-block claim classes, meaningless for any kind with no fact block to check
    /// against) with <see cref="CopyClaims.CheckClock"/>'s violations (unconditional — F138.3's own
    /// "all patter kinds"), plus — as of PLAN T342, only when <paramref name="requiredCore"/> is
    /// non-null — <see cref="CopyClaims.CheckContainment"/>'s own single violation, into ONE
    /// <see cref="ClaimCheckResult"/>. This is the whole reason a ContextSegment render can clear both
    /// claim families (and an announcement render all three) in a SINGLE gate/re-ask cycle instead of
    /// chained ladder runs (the reviewer-ruled constraint <see cref="RunTruthGateLadderAsync"/>'s own
    /// remarks restate): the ladder only ever sees one <see cref="ClaimCheckResult"/>, never two, so it
    /// has no way to run twice even by accident. All three source checks stay <see cref="CopyClaims"/>'s
    /// own pure static functions of their arguments; this method adds no logic beyond composing their
    /// violation lists.
    ///
    /// <para>
    /// <paramref name="requiredCore"/> ALSO rides <see cref="CopyClaims.CheckClock"/>'s own
    /// <c>ownerMessage</c> parameter (HIGH-2 review finding, PLAN T342 round 2), not only
    /// <see cref="CopyClaims.CheckContainment"/>'s: the SAME owner-trusted text is both "the fact this
    /// reply must contain" and "the source whose OWN present-frame weekday/daypart words the clock
    /// check must not punish" — SPEC F144.3's binding owner-trust rule, extended to the clock half
    /// exactly as it already governs the facts half via <paramref name="factBlock"/> above (which,
    /// for an announcement render, is this SAME string — see this method's own call site remarks).
    /// </para>
    /// </summary>
    static ClaimCheckResult CheckTruthGate(
        string copy, string? factBlock, DateTimeOffset stationLocalNow, string? trackTitle,
        string? requiredCore = null)
    {
        var violations = new List<ClaimViolation>();
        if (factBlock is not null)
            violations.AddRange(CopyClaims.CheckFacts(copy, factBlock).Violations);

        violations.AddRange(CopyClaims.CheckClock(copy, stationLocalNow, trackTitle, requiredCore).Violations);

        if (requiredCore is not null)
            violations.AddRange(CopyClaims.CheckContainment(copy, requiredCore).Violations);

        return new ClaimCheckResult(violations);
    }

    /// <summary>
    /// Posts one chat-completion request and returns the raw reply text (SPEC F34.3, F123.1) — the
    /// exact wire call both the first completion and the F138.4 re-ask fire (STORY-350, PLAN T331),
    /// extracted so the ladder's second call is provably the SAME request shape as the first rather
    /// than a hand-maintained second copy of the body/header/parse logic. <paramref name="ct"/> is
    /// always <c>timeoutCts.Token</c> from the one caller (<see cref="RequestCleanedCompletionAsync"/>)
    /// — this method holds no state and starts no clock of its own, so a re-ask sharing that same
    /// token shares that render's existing budget rather than getting a fresh one (F138.4's "never a
    /// longer feeder hold").
    /// </summary>
    static async Task<CompletionReply> PostCompletionAsync(
        HttpClient http, Uri requestUri, LlmOptions cfg, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var body = new
        {
            model = cfg.Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            max_tokens = DeriveMaxTokens(cfg.MaxCopyChars),
            // gh-#620: "none" by default so a thinking model answers instead of spending max_tokens
            // on chain-of-thought; null (Llm:ReasoningEffort "omit") leaves the field out entirely —
            // ChatCompletionRequestJson.Options is what turns that null into an absent member.
            reasoning_effort = ReasoningEffort.ToWire(cfg.ReasoningEffort),
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body, options: ChatCompletionRequestJson.Options),
        };

        // Bearer header rides only when an ApiKey is configured (env-only, F19.3/F34.3).
        if (!string.IsNullOrEmpty(cfg.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        }

        var response = await http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(ct);
        var choice = payload?.Choices?.FirstOrDefault();
        return new CompletionReply(
            choice?.Message?.Content ?? string.Empty,
            choice?.FinishReason,
            choice?.Message?.Reasoning?.Length ?? 0);
    }

    /// <summary>
    /// The hygiene entry for one reply (gh-#620): a reasoning-only reply (<see cref="CompletionReply.IsReasoningOnly"/>)
    /// becomes <see cref="LlmCopyCleanupResult.ReasoningOnly"/> directly — <see cref="CleanCopy"/> would
    /// only ever see an empty string and report "empty after cleanup", the wrong-lever wording that
    /// sent the 2026-08-24 bench morning at the endpoint and max_tokens for a failure only
    /// <c>Llm:ReasoningEffort</c> fixes. Every other reply runs the unchanged hygiene pass.
    /// </summary>
    static LlmCopyCleanupResult CleanupOf(CompletionReply reply, int maxCopyChars) =>
        reply.IsReasoningOnly
            ? new LlmCopyCleanupResult.ReasoningOnly(reply.ReasoningChars, reply.FinishReason)
            : CleanCopy(reply.Content, maxCopyChars);

    long ElapsedMs(DateTimeOffset startedAt) => (long)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;

    // gh-#117 — the ONE place this writer resolves "station-local now" for the prompt's clock
    // line: Station:Timezone via the live IStationClockProvider seam when the composition supplies
    // one, otherwise the container's own clock (timeProvider.LocalTimeZone) — byte-identical to
    // the pre-gh-#117 behavior for every rig that never registers the seam.
    DateTimeOffset StationLocalNow() =>
        stationClock?.LocalNow ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone);

    /// <summary>
    /// Classifies a completion fault for <see cref="LlmCallRing"/> (SPEC F73.1, F139.1): the ONE other
    /// <see cref="OperationCanceledException"/> source reaching this catch-all (the caller's own
    /// cancellation is already filtered out by the clause above) is <c>RequestCleanedCompletionAsync</c>'s
    /// own <c>timeoutCts</c> firing — <see cref="LlmCallOutcome.Timeout"/>/<see cref="LlmCallCause.Timeout"/>,
    /// distinct from a generic <see cref="LlmCallOutcome.Failed"/>/<see cref="LlmCallCause.ConnectionFailure"/>.
    /// The F139 taxonomy has no finer split for "a response arrived but was non-2xx" versus "no
    /// response ever arrived at all" — both land on <see cref="LlmCallCause.ConnectionFailure"/> here,
    /// same as they already share <see cref="LlmCallOutcome.Failed"/>. Deliberately independent of
    /// <see cref="LogFailure"/>'s own <c>detail</c> switch (SPEC F69.7) — that one feeds a WARN line
    /// and has no need to split out timeout, so duplicating this small a classification is simpler
    /// than threading a shared helper through two call sites with different needs.
    /// </summary>
    internal static (LlmCallOutcome Outcome, LlmCallCause Cause, string Detail) ClassifyForRing(Exception ex) => ex switch
    {
        OperationCanceledException => (LlmCallOutcome.Timeout, LlmCallCause.Timeout, "Llm:TimeoutSeconds exceeded"),
        HttpRequestException { StatusCode: { } status } =>
            (LlmCallOutcome.Failed, LlmCallCause.ConnectionFailure, $"HTTP {(int)status}"),
        _ => (LlmCallOutcome.Failed, LlmCallCause.ConnectionFailure, ex.GetType().Name),
    };

    /// <summary>
    /// Derives a completion request's <c>max_tokens</c> cap from a char figure
    /// (SPEC F123.1, STORY-319, PLAN T262) — see <see cref="CharsPerTokenDivisor"/>,
    /// <see cref="MinGenerationTokenCap"/>, and <see cref="MaxGenerationTokenCap"/> for why the
    /// divisor, floor, and ceiling are what they are. Applied identically to the on-air path and
    /// the preview path (both fed <see cref="LlmOptions.MaxCopyChars"/>, funnelled through
    /// <see cref="RequestCleanedCompletionAsync"/>'s single request-builder). Internal (PLAN T282,
    /// SPEC F127.3, T283 paper-audition reconciliation) — <see cref="CrosstalkScriptWriter"/> reuses
    /// this exact chars-to-tokens SHAPE for its own whole-script cap rather than a second,
    /// independently-tuned formula, but feeds it a char figure derived from
    /// <c>Crosstalk:DurationTargetSeconds</c>, not <c>Llm:MaxCopyChars</c> — a blurb-scaled figure
    /// starves a multi-line script (see <c>CrosstalkScriptWriter.DeriveScriptGenerationCap</c>'s own
    /// remarks for the T283 finding).
    /// </summary>
    internal static int DeriveMaxTokens(int maxCopyChars) =>
        Math.Clamp(maxCopyChars / CharsPerTokenDivisor, MinGenerationTokenCap, MaxGenerationTokenCap);

    /// <summary>
    /// Extracts the airable/previewable text from a <see cref="LlmCopyCleanupResult"/> (SPEC
    /// F123.2): an exact fit and a salvaged trim both hand back real copy — the caller cannot tell
    /// (and does not need to) which one it got, since both are genuinely LLM-authored — and only a
    /// full reject (hygiene's own, or the F138.4 ladder's <see cref="LlmCopyCleanupResult.TruthGateRejected"/>
    /// floor, PLAN T331) hands back null, so the caller degrades exactly as it did before T263. Shared
    /// by <see cref="WriteAsync"/> and <see cref="WritePreviewAsync"/> so the two can never read the
    /// closed hierarchy differently.
    /// </summary>
    static string? TextOf(LlmCopyCleanupResult cleanup) => cleanup switch
    {
        LlmCopyCleanupResult.Fits fits => fits.Text,
        LlmCopyCleanupResult.Trimmed trimmed => trimmed.Text,
        LlmCopyCleanupResult.Rejected => null,
        LlmCopyCleanupResult.ReasoningOnly => null,
        LlmCopyCleanupResult.TruthGateRejected => null,
        _ => throw new UnreachableException($"Unhandled {nameof(LlmCopyCleanupResult)} case."),
    };

    /// <summary>
    /// Names the real cause of a null <see cref="TextOf"/> result for <see cref="WriteAsync"/>'s own
    /// failure WARN (SPEC F69.7, T331 review finding F3, generalized PLAN T332): a hygiene reject
    /// splits on <see cref="LlmCopyCleanupResult.Rejected.WasOverLength"/> exactly as it always has,
    /// but a <see cref="LlmCopyCleanupResult.TruthGateRejected"/> floor gets its OWN sentence naming
    /// the truth gate and every still-violating claim — never the hygiene wording ("empty or exceeded
    /// Llm:MaxCopyChars after cleanup"), which sends an operator at the wrong levers (endpoint,
    /// max_tokens) for a failure neither lever can fix.
    ///
    /// <para>
    /// No longer names "the context fact gate" specifically (T331 pickup, PLAN T332 — the wording was
    /// hardcoded for STORY-350's single check before STORY-351's clock check reused this same floor):
    /// <see cref="DescribeViolationForLog"/> renders each violation HONESTLY per its own class —
    /// <see cref="ClaimViolation.IsClockClaim"/> true (a weekday) reads "wrong-day claim", true (a
    /// daypart) reads "wrong-time-of-day claim"; false (a fact-block violation) reads "unsupported
    /// claim" — so a clock rejection is never misreported as a fact-block one or vice versa, whichever
    /// kind's ladder floor produced it.
    /// </para>
    ///
    /// Comma-free, sentence-fragment style (matches every other <see cref="LogFailure"/> reason) —
    /// this never reaches prompt text, only a log line, but <see cref="ClaimViolation.Token"/> stays
    /// safe to interpolate directly regardless (that type's own remarks: provably digit-shaped or
    /// closed-vocabulary).
    /// </summary>
    // reasoningEffort is rendered through ReasoningEffort.ToWire (closed vocabulary), never raw — this
    // text reaches the authenticated preview surface (WritePreviewAsync's own "never free text" rule).
    static string DescribeNullTextReason(LlmCopyCleanupResult cleanup, string reasoningEffort) => cleanup switch
    {
        LlmCopyCleanupResult.Rejected { WasOverLength: true } => "exceeded Llm:MaxCopyChars after cleanup",
        LlmCopyCleanupResult.Rejected { WasOverLength: false } => "empty after cleanup",
        // gh-#620 — names the real cause AND the one lever that fixes it; "empty after cleanup" would
        // send the operator at the endpoint and max_tokens, neither of which can help.
        LlmCopyCleanupResult.ReasoningOnly reasoningOnly =>
            $"no answer, only {reasoningOnly.ReasoningChars} chars of reasoning (finish_reason: {reasoningOnly.FinishReason ?? "n/a"}) — " +
            $"a thinking model spent max_tokens on chain-of-thought; Llm:ReasoningEffort is '{ReasoningEffort.ToWire(reasoningEffort) ?? ReasoningEffort.Omit}' (\"none\" stops it)",
        LlmCopyCleanupResult.TruthGateRejected truthGate =>
            "the truth gate rejected the re-ask too (" +
            $"{string.Join(" and ", truthGate.Violations.Select(DescribeViolationForLog).Distinct(StringComparer.OrdinalIgnoreCase))})",
        _ => throw new UnreachableException(
            $"{nameof(TextOf)} already returns non-null text for any other {nameof(LlmCopyCleanupResult)} case."),
    };

    /// <summary>
    /// One violation's own honest fragment for <see cref="DescribeNullTextReason"/> (SPEC F138.2,
    /// F138.3, F144.3, PLAN T332, T342): <see cref="ClaimViolation.Class"/> is checked FIRST for
    /// <see cref="ClaimClass.AnnouncementCore"/> (PLAN T342 — the opposite-direction violation, a
    /// missing requirement rather than an unsupported addition, so it earns its own honest wording
    /// rather than falling into "unsupported claim"), then <see cref="ClaimViolation.IsClockClaim"/> is
    /// the discriminator this method (and <see cref="LlmPromptBuilder.DescribeViolationForReask"/>, one
    /// module over) keys the facts-vs-clock split on for everything else — see that property's own
    /// remarks. Within the clock branch, <paramref name="violation"/>'s own <see cref="ClaimClass"/>
    /// still separates the two clock claim shapes: a wrong weekday reads "wrong-day claim", a wrong
    /// daypart reads "wrong-time-of-day claim".
    /// </summary>
    static string DescribeViolationForLog(ClaimViolation violation) => violation switch
    {
        { Class: ClaimClass.AnnouncementCore } => $"dropped announcement core: {violation.Token}",
        { IsClockClaim: true, Class: ClaimClass.Daypart } => $"wrong-time-of-day claim: {violation.Token}",
        { IsClockClaim: true } => $"wrong-day claim: {violation.Token}",
        _ => $"unsupported claim: {violation.Token}",
    };

    /// <summary>
    /// Maps a <see cref="LlmCopyCleanupResult"/> to its ring outcome/cause pair (SPEC F73.1, F139.1)
    /// — extracted (STORY-350, PLAN T331) so <see cref="RequestCleanedCompletionAsync"/>'s ordinary
    /// success path and its F138.4 re-ask path share ONE classification rather than two hand-kept
    /// copies of the same switch. Ok still records the RAW reply regardless of outcome — a full
    /// reject (empty/no-sentence-fits) stays Ok exactly as before T263 (a hygiene decision the caller
    /// makes, not a fact about whether the call itself succeeded; see <see cref="LlmCallOutcome.Ok"/>'s
    /// own remarks); a trim gets its own finer-grained outcome instead (<see cref="LlmCallOutcome.Trimmed"/>).
    /// The additive Cause field (STORY-353, PLAN T330) splits the SAME three shapes finer still — a
    /// Trimmed salvage still aired, so it is Success exactly like an exact Fits; a full Rejected
    /// splits on WHY nothing survived (<see cref="LlmCopyCleanupResult.Rejected.WasOverLength"/>,
    /// decided once at <see cref="CleanCopy"/>, never re-derived here). Never called for a cleanup the
    /// F138.2 truth gate already rejected — that path stamps <see cref="LlmCallCause.TruthGateReject"/>
    /// itself, bypassing this map entirely (see that call site's own remarks).
    /// </summary>
    static (LlmCallOutcome Outcome, LlmCallCause Cause) ClassifyCleanup(LlmCopyCleanupResult cleanup) => cleanup switch
    {
        LlmCopyCleanupResult.Trimmed => (LlmCallOutcome.Trimmed, LlmCallCause.Success),
        LlmCopyCleanupResult.Fits => (LlmCallOutcome.Ok, LlmCallCause.Success),
        LlmCopyCleanupResult.Rejected { WasOverLength: true } => (LlmCallOutcome.Ok, LlmCallCause.OverLength),
        LlmCopyCleanupResult.Rejected { WasOverLength: false } => (LlmCallOutcome.Ok, LlmCallCause.EmptyCompletion),
        // gh-#620: the F139 taxonomy is unchanged — a reasoning-only reply IS an empty completion to
        // the ring and the tile; the WARN (DescribeNullTextReason) is where the finer cause is named.
        LlmCopyCleanupResult.ReasoningOnly => (LlmCallOutcome.Ok, LlmCallCause.EmptyCompletion),
        _ => throw new UnreachableException($"Unhandled {nameof(LlmCopyCleanupResult)} case."),
    };

    /// <summary>
    /// The hygiene pass every LLM-authored line in this project runs through (SPEC F34.5): trims,
    /// strips a chat preamble, unwraps one layer of wrapping quotes, collapses newlines to spaces,
    /// and strips stage directions and markdown emphasis markers. Deliberately excludes the F123.2
    /// sentence-boundary SALVAGE below — that is a length-policy decision <see cref="CleanCopy"/>
    /// layers on top for the ordinary on-air/preview path, and <see cref="CrosstalkScriptWriter"/>'s
    /// own per-line validation (SPEC F127.4: cleared, never trimmed) needs the SAME text transform
    /// with a completely different length policy (reject the whole exchange, never cut a line).
    /// Internal (PLAN T282 extraction) — a small shared helper rather than a second hand-maintained
    /// copy of these five steps, with zero change to <see cref="CleanCopy"/>'s own byte-for-byte
    /// output (a pure extract-method refactor).
    /// </summary>
    internal static string ApplyCopyHygiene(string raw)
    {
        var text = StripChatPreamble(raw.Trim());   // gh-#186 — must run BEFORE quote unwrapping
        text = StripWrappingQuotes(text);
        text = NewlinePattern.Replace(text, " ");
        text = BracketStageDirectionPattern.Replace(text, string.Empty);
        text = AsteriskStageDirectionPattern.Replace(text, string.Empty);
        text = MarkdownEmphasisPattern.Replace(text, string.Empty);
        return RepeatedWhitespacePattern.Replace(text, " ").Trim();
    }

    /// <summary>
    /// Copy hygiene (<see cref="ApplyCopyHygiene"/>, SPEC F34.5) plus the F123.2 sentence-boundary
    /// salvage (STORY-319, PLAN T263): a result that still exceeds <paramref name="maxChars"/> after
    /// hygiene is no longer an automatic reject — it is cut at the LAST complete sentence that fits
    /// under the cap (see <see cref="TrimToLastCompleteSentence"/>), never mid-sentence, and never at
    /// an abbreviation's own period (see that method's own remarks).
    /// <see cref="LlmCopyCleanupResult.Rejected"/> is reserved for the cases nothing salvages:
    /// hygiene left an empty string, or nothing complete under the cap survives that filter — no
    /// candidate at all, or every candidate under the cap was an abbreviation/lone-initial period.
    /// </summary>
    static LlmCopyCleanupResult CleanCopy(string raw, int maxChars)
    {
        var text = ApplyCopyHygiene(raw);

        if (text.Length == 0)
            return new LlmCopyCleanupResult.Rejected(WasOverLength: false);

        if (text.Length <= maxChars)
            return new LlmCopyCleanupResult.Fits(text);

        var salvaged = TrimToLastCompleteSentence(text, maxChars);
        return salvaged is null
            ? new LlmCopyCleanupResult.Rejected(WasOverLength: true)
            : new LlmCopyCleanupResult.Trimmed(salvaged, CharsBeforeTrim: text.Length);
    }

    /// <summary>
    /// SPEC F123.2 (STORY-319, PLAN T263) — salvages over-length copy by cutting at the LAST complete
    /// sentence that still fits under <paramref name="maxChars"/>, never mid-sentence: every candidate
    /// cut point comes from <see cref="SentenceBoundaryPattern"/>, so the returned text always ends
    /// exactly at a terminator (plus its optional closing quote), by construction. A candidate whose
    /// period is an abbreviation's or a lone initial's (see <see cref="IsAbbreviationBoundary"/>) is
    /// skipped — not treated as a reject-everything signal — so the cut FALLS THROUGH to whichever
    /// earlier candidate already fit (review finding: the "St. Vincent" shape has a genuine sentence
    /// boundary before the abbreviation period, and discarding it in favor of the later, wrong one
    /// is exactly the bug this guards against). Returns null when nothing survives that filter under
    /// the cap — either no candidate at all, or every candidate under the cap was rejected — so the
    /// caller falls back to the pre-F123 reject.
    /// </summary>
    static string? TrimToLastCompleteSentence(string text, int maxChars)
    {
        string? lastFit = null;
        foreach (Match match in SentenceBoundaryPattern.Matches(text))
        {
            var cutAt = match.Index + match.Length;
            if (cutAt > maxChars)
                break;   // matches occur in text order — every later one only extends further

            if (IsAbbreviationBoundary(text, match.Index))
                continue;   // not a real sentence end — keep whatever earlier candidate already fit

            lastFit = text[..cutAt];
        }

        return lastFit;
    }

    /// <summary>
    /// Rejects a candidate boundary whose terminator is an abbreviation's or a lone initial's period
    /// (SPEC F123.2 review finding) — <c>!</c>/<c>?</c> never carry this ambiguity and are never
    /// rejected here. Two shapes are checked against the word immediately before the period (the
    /// maximal run of letters scanning backward, stopping at the first non-letter — this is exactly
    /// the "S" in "U.S." too, since the internal period between "U" and "S" is itself a non-letter):
    /// a known abbreviation (<see cref="KnownAbbreviations"/>, case-insensitive — "St.", "Dr."), or a
    /// LONE INITIAL — a single letter whose own preceding character is neither a letter nor an
    /// apostrophe. That apostrophe exclusion is deliberate: without it, a contraction's own final
    /// letter ("don't.", "isn't.") would misread as a one-letter "initial", since the character
    /// immediately before it is punctuation, not a letter — the apostrophe is what tells the two
    /// shapes apart. "U.S." falls out of the SAME lone-initial check with no separate rule: the
    /// character before the "S" is the internal period, which is neither a letter nor an apostrophe.
    /// </summary>
    static bool IsAbbreviationBoundary(string text, int terminatorIndex)
    {
        if (text[terminatorIndex] != '.')
            return false;

        var wordEnd = terminatorIndex;
        var wordStart = wordEnd;
        while (wordStart > 0 && char.IsLetter(text[wordStart - 1]))
            wordStart--;

        if (wordStart == wordEnd)
            return false;   // nothing letter-like immediately precedes the period at all

        var word = text[wordStart..wordEnd];
        if (KnownAbbreviations.Contains(word))
            return true;

        if (word.Length != 1)
            return false;

        var beforeWordIndex = wordStart - 1;
        return beforeWordIndex < 0 || !IsApostrophe(text[beforeWordIndex]);
    }

    static bool IsApostrophe(char c) => c is '\'' or '’';

    /// <summary>
    /// Folds the curly apostrophe (U+2019, RIGHT SINGLE QUOTATION MARK) to the straight ASCII one
    /// (U+0027) — the SAME single-glyph pair <see cref="IsApostrophe"/> already treats as one mark,
    /// char-by-char, in <see cref="IsAbbreviationBoundary"/> above, mirrored here as a whole-string
    /// fold rather than a second, differently-shaped apostrophe rule. Internal (HIGH-1, PLAN T342
    /// review) — <see cref="CopyClaims.CheckContainment"/> is the second caller, reusing this exact
    /// discipline so its own substring test folds BOTH the aired copy and the owner's required core
    /// identically before comparing them (that method's own remarks explain why); mirrors
    /// <see cref="ApplyCopyHygiene"/>'s own "internal precisely for a second caller" posture one
    /// member up. Deliberately narrower than <see cref="SpeechText.Normalize"/>'s own curly-quote fold
    /// (which also folds the LEFT single quote, U+2018, and runs downstream of this checker by
    /// design — this class's own remarks) — only U+2019 ever marks an apostrophe in this writer's own
    /// prompt/regex vocabulary (<see cref="PresentFrameWeekdayRx"/>/<see cref="PresentFrameDaypartRx"/>'s
    /// own standing keep-both comments), so this fold stays scoped to that one glyph rather than
    /// silently adopting a second normalization rule it was never asked to match.
    /// </summary>
    internal static string FoldApostrophes(string text) => text.Replace('’', '\'');

    /// <summary>
    /// gh-#186, widened by gh-#430: drops a chat preamble in front of the copy — observed live
    /// rendering the preamble to air, both in the originally-fixed quoted-body shape ("Here's
    /// your lead-in copy:" followed by a quoted body) and, per gh-#430, with an entirely UNQUOTED
    /// body ("Here is the lead-in announcement: Tonight we're taking a detour..."). Two
    /// conditions must both hold before anything is dropped, so legitimate announcer copy that
    /// merely contains a colon survives untouched: the first colon comes early (≤80 chars) with no
    /// quote or line break before it, and the preamble contains a meta word (see
    /// <see cref="PreambleMetaWordPattern"/> — a model talking ABOUT the copy; ordinary phrasing
    /// like "Up next:" has none and is kept). This pair is the whole heuristic and is deliberately
    /// not widened further (gh-#430 review note) — it is what keeps legit copy such as
    /// <c>Coming up at nine: a jazz number</c> intact.
    ///
    /// Once both gates hold, the preamble is gone: everything after the colon, trimmed, is what
    /// remains, whatever shape the body takes. A body that is one quoted block running to the very
    /// end (the gh-#186 shape) is returned still wrapped — <see cref="StripWrappingQuotes"/>
    /// unwraps it next. A body with NO quotes at all (gh-#430) is returned as-is; there is nothing
    /// left to unwrap. A body that OPENS with a quote but has trailing text after the closing one
    /// (<c>Sure: "Great tune" - and plenty more where that came from.</c>) is also returned as-is —
    /// <see cref="StripWrappingQuotes"/> only unwraps a string that IS entirely one quoted block,
    /// so the embedded quote marks ride through untouched, which is the sensible reading: the chat
    /// preamble is gone, and the quoted title inside the body stays exactly as quoted. A body under
    /// 2 chars after trimming is treated as no real body at all, and the original text survives.
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
        return body.Length < 2 ? text : body;
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
