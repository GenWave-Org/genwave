namespace GenWave.Tts;

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Generates one ad spot script per call (SPEC F160.1, F160.2, STORY-390 AC2/AC3) — the GenWave.Tts
/// half of the ad-authoring feature: its OWN authoring flow, never <c>ISegmentCopyWriter</c> (the
/// <see cref="CrosstalkScriptWriter"/> precedent one seam over). Sampling WHICH enabled
/// <c>ad_brief</c> row to write from, validating the raw script, and rendering audio are ALL later or
/// caller concerns this class never touches — <see cref="AdScriptWriteRequest"/> arrives already
/// resolved, and validation arrives as an opaque delegate (see this class's own "GenWave.Tts must
/// never reference GenWave.Ads" remarks below).
///
/// <para>
/// <b>One completion per ATTEMPT, at most two attempts (SPEC F160.3's ladder shape).</b> Unlike
/// <see cref="CrosstalkScriptWriter"/>'s zero-re-ask "skip-only" posture, an ad script gets exactly ONE
/// re-ask naming the violated rule when its first draft fails <c>validate</c> — the F138 ladder shape
/// one seam over, generalized here to an arbitrary caller-supplied check rather than the F138.2 truth
/// gate specifically. A SECOND violation, or any transport/generation fault at EITHER attempt (a
/// disabled endpoint, a timeout, a <c>finish_reason: length</c> truncation, an empty completion after
/// hygiene), fails the whole spot immediately — F160.1 is skip-only, no template floor: a canned
/// parody ad is worse than none. Which of the two branches an attempt took (PLAN T400 review F6) is
/// carried structurally through <see cref="AdScriptAttemptOutcome"/> — <see cref="WriteAsync"/>'s own
/// re-ask decision matches on that TYPE, never on <see cref="AdScriptWriteResult.Failed.RuleId"/>
/// being non-null (a nullability check a future field addition could silently break).
/// </para>
///
/// <para>
/// <b>GenWave.Tts must never reference GenWave.Ads (L10).</b> The real <c>AdScriptValidator</c>
/// (GenWave.Ads) is handed in as <c>validate</c> — a delegate the caller (T402's own <c>AdSpotWorker</c>,
/// itself living in GenWave.Ads) builds by closing over
/// <c>AdScriptValidator.Validate(rawScript, AdScriptValidationRequest, durationEstimator)</c>.
/// <see cref="AdScriptValidationOutcome"/> is the minimal contract that crossing keeps honest — see its
/// own remarks for why a caller needing the parsed <c>AdScript</c> lines simply re-validates the
/// returned raw text once more, rather than this writer ever holding a reference to that type. A
/// refusal's <see cref="AdScriptValidationOutcome.Refused.Reason"/> arrives from that SAME arbitrary
/// delegate, so <see cref="BoundReason"/> defensively truncates/sanitizes it (PLAN T400 review F7)
/// before it ever reaches the re-ask prompt or the ring's <c>StatusDetail</c> — this writer never
/// trusts a caller-supplied delegate to have already bounded its own text.
/// </para>
///
/// <para>
/// Every backend call reuses the SAME wire seam <see cref="LlmCopyWriter.PostChatCompletionAsync"/>
/// (PLAN T400 review F5) — never a third hand-rolled copy of the body/header/parse block (the
/// duplicate that caused review finding F1). <see cref="DeriveScriptGenerationCap"/> derives the
/// <c>max_tokens</c> cap from <see cref="AdScriptWriteRequest.SpotSeconds"/> AND
/// <see cref="AdScriptWriteRequest.ToleranceRatio"/>, never <see cref="LlmOptions.MaxCopyChars"/>; the
/// raw reply is cleaned by <see cref="ApplyLineAwareHygiene"/>, NOT
/// <see cref="LlmCopyWriter.ApplyCopyHygiene"/> run on the whole multi-line reply directly — see that
/// method's own remarks for why (PLAN T400 review F1 BLOCKER: hygiene's newline-collapse contract is
/// built for a one-line blurb and destroys a multi-voice script's own line structure). No single-flight
/// gate here, the SAME reasoning <see cref="CrosstalkScriptWriter"/>'s own remarks give: ad generation
/// happens entirely off the on-air clock (T401's <c>AdSpotWorker</c> tick, SPEC F161.1) — the natural
/// place to coordinate backend concurrency if that ever proves necessary; adding a shared gate here
/// now, with no caller yet, would be speculative. EACH attempt gets its OWN <c>Llm:TimeoutSeconds</c>
/// budget (gh-#696, the first-contact finding — built inline in <see cref="WriteAsync"/>):
/// the on-air writers (<see cref="LlmCopyWriter.PostChatCompletionAsync"/>'s re-ask callers) share
/// one budget across a re-ask because the break they write for is imminent, but this writer runs
/// entirely off the air clock — the same fact that justifies the missing single-flight gate — and on
/// the reference station's CPU-bound 3B model one completion took 49 of a 90-second shared budget, so
/// the re-ask timed out by construction on every tick and the stock pass yielded nothing.
/// </para>
///
/// <para>
/// Registered as a DI singleton with NO eager I/O in its constructor (Story125's zero-I/O invariant,
/// the same posture <see cref="CrosstalkScriptWriter"/>'s own remarks document) — every dependency
/// here is itself a cheap seam, so constructing this class never touches the network.
/// </para>
/// </summary>
public sealed partial class AdScriptWriter(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<LlmOptions> llmOptions,
    LlmCallRecorder recorder,
    IDegradationModeReader degradationMode,
    ILogger<AdScriptWriter> logger,
    TimeProvider timeProvider)
{
    /// <summary>Cap for a validator Reason before it reaches the re-ask prompt or the ring's
    /// <c>StatusDetail</c> (PLAN T400 review F7) — the <c>CrosstalkScriptParser.TruncateForEcho</c>/
    /// <c>AdScriptParser.EchoForReason</c> precedent (CWE-117 log forging, an unbounded echo from an
    /// untrusted source).</summary>
    const int MaxEchoedReasonChars = 120;

    /// <summary>
    /// Headroom the whole-script generation cap carries OVER the validator's own duration CEILING
    /// (<c>SpotSeconds × (1 + ToleranceRatio)</c>) — mirrors <see cref="CrosstalkScriptWriter"/>'s own
    /// <c>GenerationCapHeadroomMultiplier</c> exactly, for the identical reason: the cap exists to stop
    /// runaway rambling, not to enforce the duration target itself. Widened over the CEILING (not the
    /// raw <see cref="AdScriptWriteRequest.SpotSeconds"/> target the prompt's own stated budget uses)
    /// so a script the validator would genuinely ACCEPT — one that ran up to the full tolerance — is
    /// never truncated by <c>max_tokens</c> before <c>validate</c> ever sees it.
    /// </summary>
    const double GenerationCapHeadroomMultiplier = 2.0;

    static int DeriveScriptGenerationCap(AdScriptWriteRequest request)
    {
        var ceilingChars =
            request.SpotSeconds * (1 + request.ToleranceRatio) * CrosstalkScriptParser.CharsPerSecond;
        return LlmCopyWriter.DeriveMaxTokens((int)(ceilingChars * GenerationCapHeadroomMultiplier));
    }

    /// <summary>
    /// Writes one spot script, validating between attempts through <paramref name="validate"/> (the
    /// caller's own closure over <c>AdScriptValidator.Validate</c>, GenWave.Ads — see this class's own
    /// remarks). Never throws toward the caller for anything short of <paramref name="ct"/> itself
    /// cancelling — every other fault resolves to <see cref="AdScriptWriteResult.Failed"/> (SPEC
    /// F160.1's skip-only failure mode).
    /// </summary>
    public async Task<AdScriptWriteResult> WriteAsync(
        AdScriptWriteRequest request, Func<string, AdScriptValidationOutcome> validate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validate);

        var mode = degradationMode.CurrentMode;
        var cfg = llmOptions.CurrentValue;

        if (string.IsNullOrEmpty(cfg.Endpoint))
        {
            // The archetypal pre-flight refusal (mirrors CrosstalkScriptWriter's own SPEC F140 review
            // finding F3 handling) — zero I/O, so systemPrompt: null skips the ring record below:
            // nothing was ever attempted.
            return Failed(
                "Llm:Endpoint is not configured", ruleId: null, LlmCallCause.ConnectionFailure, request.SponsorName,
                timeProvider.GetUtcNow(), mode, systemPrompt: null, userPrompt: null, cfg.Model);
        }

        var systemPrompt = AdScriptPromptBuilder.BuildSystemPrompt(request);
        var userPrompt = AdScriptPromptBuilder.BuildUserContent(request);

        // EACH attempt gets its OWN Llm:TimeoutSeconds budget (gh-#696 — see the class remarks: this
        // writer is off the air clock, so the on-air writers' shared-budget shape starved the re-ask).
        // The client/URI/generation cap are still resolved ONCE: both attempts target the same
        // endpoint with the same cap.
        var http = httpClientFactory.CreateClient(LlmCopyWriter.HttpClientName);
        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");
        var maxTokens = DeriveScriptGenerationCap(request);

        // The budget rides the injected TimeProvider (STORY-442, gh-#723) so a fake clock can fire it;
        // CancelAfter has no TimeProvider overload, so this mirrors DependencyHealthProber.ProbeOneAsync.
        using var firstBudget = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.TimeoutSeconds), timeProvider);
        using var firstLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, firstBudget.Token);
        var firstAttempt = await AttemptAsync(
            http, requestUri, cfg, request, systemPrompt, userPrompt, maxTokens, validate, mode, ct, firstLinked.Token);
        if (firstAttempt is not AdScriptAttemptOutcome.ValidatorRefused refused)
            return ResultOf(firstAttempt); // Success, or a transport/generation fault — never re-asked (skip-only).

        // SPEC F160.3's ladder shape: exactly ONE re-ask, naming the violated rule, appended to the
        // SAME user prompt the rejected draft already saw. A fresh budget pair (gh-#696) — never
        // shared with the first attempt's.
        var reaskUserPrompt = userPrompt + "\n\n" + AdScriptPromptBuilder.BuildReaskLine(refused.RuleId, refused.Reason);
        using var reaskBudget = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.TimeoutSeconds), timeProvider);
        using var reaskLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, reaskBudget.Token);
        var secondAttempt = await AttemptAsync(
            http, requestUri, cfg, request, systemPrompt, reaskUserPrompt, maxTokens, validate, mode, ct, reaskLinked.Token);
        return ResultOf(secondAttempt);
    }

    static AdScriptWriteResult ResultOf(AdScriptAttemptOutcome outcome) => outcome switch
    {
        AdScriptAttemptOutcome.Resolved resolved => resolved.Result,
        AdScriptAttemptOutcome.ValidatorRefused refused => refused.Result,
        _ => throw new UnreachableException($"Unhandled {nameof(AdScriptAttemptOutcome)} case."),
    };

    async Task<AdScriptAttemptOutcome> AttemptAsync(
        HttpClient http, Uri requestUri, LlmOptions cfg, AdScriptWriteRequest request, string systemPrompt,
        string userPrompt, int maxTokens, Func<string, AdScriptValidationOutcome> validate, DegradationMode mode,
        CancellationToken ct, CancellationToken linkedCt)
    {
        var startedAt = timeProvider.GetUtcNow();

        try
        {
            var reply = await LlmCopyWriter.PostChatCompletionAsync(
                http, requestUri, cfg, systemPrompt, userPrompt, maxTokens, linkedCt);

            // The SAME gh-#424-class check CrosstalkScriptWriter runs before its own parser ever sees
            // a reply — a completion cut short at its own max_tokens cap can still LOOK well-formed.
            if (reply.FinishReason == "length")
            {
                return Resolved(Failed(
                    "the completion was cut short by max_tokens (finish_reason: length) — a truncated script is never aired",
                    ruleId: null, LlmCallCause.OverLength, request.SponsorName, startedAt, mode, systemPrompt, userPrompt,
                    cfg.Model, reply.Content));
            }

            var cleaned = ApplyLineAwareHygiene(reply.Content);
            cleaned = ApplyPhoneHygiene(cleaned, request.Phone);
            if (cleaned.Length == 0)
            {
                return Resolved(Failed(
                    "the completion was empty after cleanup", ruleId: null, LlmCallCause.EmptyCompletion,
                    request.SponsorName, startedAt, mode, systemPrompt, userPrompt, cfg.Model, reply.Content));
            }

            return validate(cleaned) switch
            {
                AdScriptValidationOutcome.Accepted => Resolved(
                    Accept(cleaned, request.SponsorName, systemPrompt, userPrompt, reply.Content, startedAt, mode, cfg.Model)),
                AdScriptValidationOutcome.Refused refused => BuildRefused(
                    refused, request.SponsorName, startedAt, mode, systemPrompt, userPrompt, cfg.Model, reply.Content),
                _ => throw new UnreachableException($"Unhandled {nameof(AdScriptValidationOutcome)} case."),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled (e.g. shutdown) — not our own Llm:TimeoutSeconds budget expiring,
            // and not a call outcome worth a ring entry (mirrors CrosstalkScriptWriter's own handling).
            throw;
        }
        catch (Exception ex)
        {
            var (outcome, cause, detail) = LlmCopyWriter.ClassifyForRing(ex);

            recorder.Record(
                request.SponsorName, systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode, cause, cfg.Model, LlmCallKind.AdScript);
            logger.LogInformation(
                "Ad script failed (brand: {Brand}): {Detail}",
                request.SponsorName.ReplaceLineEndings(" "), detail.ReplaceLineEndings(" "));
            return Resolved(new AdScriptWriteResult.Failed(detail, RuleId: null, cause));
        }
    }

    static AdScriptAttemptOutcome Resolved(AdScriptWriteResult result) => new AdScriptAttemptOutcome.Resolved(result);

    /// <summary>
    /// Wraps a validator refusal into its own <see cref="AdScriptAttemptOutcome.ValidatorRefused"/>
    /// (PLAN T400 review F6 — the re-ask decision matches on THIS type, never on
    /// <see cref="AdScriptWriteResult.Failed.RuleId"/> being non-null) — bounds
    /// <paramref name="refused"/>'s own <see cref="AdScriptValidationOutcome.Refused.Reason"/> exactly
    /// ONCE here (PLAN T400 review F7), so both the re-ask prompt (<see cref="WriteAsync"/>'s own
    /// caller, reading <see cref="AdScriptAttemptOutcome.ValidatorRefused.Reason"/>) and the ring's
    /// <c>StatusDetail</c> (via <see cref="Failed"/> below) see the SAME already-bounded text, never
    /// two independently-truncated copies. <see cref="MapRuleIdToCause"/> is the honest per-rule F139
    /// mapping (PLAN T400 review F4).
    /// </summary>
    AdScriptAttemptOutcome BuildRefused(
        AdScriptValidationOutcome.Refused refused, string brand, DateTimeOffset startedAt, DegradationMode mode,
        string systemPrompt, string userPrompt, string model, string raw)
    {
        var boundedReason = BoundReason(refused.Reason);
        var cause = MapRuleIdToCause(refused.RuleId);
        var failed = Failed(boundedReason, refused.RuleId, cause, brand, startedAt, mode, systemPrompt, userPrompt, model, raw);
        return new AdScriptAttemptOutcome.ValidatorRefused(refused.RuleId, boundedReason, failed);
    }

    AdScriptWriteResult Accept(
        string script, string brand, string systemPrompt, string userPrompt, string raw,
        DateTimeOffset startedAt, DegradationMode mode, string model)
    {
        recorder.Record(
            brand, systemPrompt, userPrompt, raw, startedAt, ElapsedMs(startedAt),
            LlmCallOutcome.Ok, statusDetail: null, mode, LlmCallCause.Success, model, LlmCallKind.AdScript);
        return new AdScriptWriteResult.Success(script);
    }

    /// <summary>
    /// The one failure path every reject funnels through (mirrors <c>CrosstalkScriptWriter.Discard</c>) —
    /// records into <see cref="LlmCallRecorder"/> (skipped entirely when <paramref name="systemPrompt"/>
    /// is null, i.e. nothing was ever attempted — the disabled-endpoint short-circuit) and logs exactly
    /// one Information line (never WARN — F160.1's own posture: a failed generation is discipline, not
    /// an outage). <paramref name="cause"/> is decided by the CALLER, at the point it already knows why —
    /// this method never inspects <paramref name="reason"/>'s text.
    /// </summary>
    AdScriptWriteResult.Failed Failed(
        string reason, string? ruleId, LlmCallCause cause, string brand, DateTimeOffset startedAt, DegradationMode mode,
        string? systemPrompt, string? userPrompt, string model, string? raw = null)
    {
        if (systemPrompt is not null)
        {
            recorder.Record(
                brand, systemPrompt, userPrompt, raw, startedAt, ElapsedMs(startedAt),
                LlmCallOutcome.Rejected, reason, mode, cause, model, LlmCallKind.AdScript);
        }

        logger.LogInformation(
            "Ad script failed (brand: {Brand}): {Reason}", brand.ReplaceLineEndings(" "), reason.ReplaceLineEndings(" "));
        return new AdScriptWriteResult.Failed(reason, ruleId, cause);
    }

    /// <summary>
    /// Maps a validator's own rule id to the F139 cause this writer stamps into the ring (SPEC F139.1,
    /// F160.3, PLAN T400 review F4) — honest per rule, the SAME shape <c>CrosstalkScriptParser</c>'s own
    /// reject branches already use (a shape mistake is <see cref="LlmCallCause.MalformedResponse"/>, a
    /// length/duration miss is <see cref="LlmCallCause.OverLength"/>, a content-truth-shaped miss is
    /// <see cref="LlmCallCause.TruthGateReject"/>) rather than flattening every refusal to one bucket.
    /// The six rule id tokens are <c>GenWave.Ads.AdScriptRuleIds</c>' own wire vocabulary, duplicated
    /// here as literal strings — this project cannot reference that one (L10) — mirrors
    /// <c>AdScriptPromptBuilder</c>'s own <c>AnnouncerTag</c> duplication for the identical reason. An
    /// unrecognized rule id (a rule <c>GenWave.Ads</c> adds later without a matching update here) falls
    /// back to <see cref="LlmCallCause.TruthGateReject"/> — the safest generic "the validator rejected
    /// content the completion produced" bucket, never <see cref="LlmCallCause.MalformedResponse"/>/
    /// <see cref="LlmCallCause.OverLength"/>, which would send an operator at the wrong lever for a
    /// cause this method does not actually recognize.
    /// </summary>
    static LlmCallCause MapRuleIdToCause(string ruleId) => ruleId switch
    {
        "format" => LlmCallCause.MalformedResponse,
        "stage_direction" => LlmCallCause.MalformedResponse,
        "duration" => LlmCallCause.OverLength,
        "brand_collision" => LlmCallCause.TruthGateReject,
        "phone_shape" => LlmCallCause.TruthGateReject,
        "audience_posture" => LlmCallCause.TruthGateReject,
        _ => LlmCallCause.TruthGateReject,
    };

    /// <summary>
    /// Bounds an untrusted validator <see cref="AdScriptValidationOutcome.Refused.Reason"/> before it
    /// reaches the re-ask prompt or the ring's <c>StatusDetail</c> (PLAN T400 review F7) — the
    /// <c>CrosstalkScriptParser.TruncateForEcho</c>/<c>AdScriptParser.EchoForReason</c> precedent
    /// (CWE-117 log forging, an unbounded echo). <see cref="AdScriptValidationOutcome.Refused.Reason"/>
    /// arrives from an ARBITRARY caller-supplied delegate (SPEC F160.1's own design — this writer never
    /// controls what a caller's validator, real or fake, puts in this field), so bounding happens here
    /// defensively rather than trusting the delegate's own discipline; <c>GenWave.Ads.AdScriptValidator</c>
    /// already bounds its own violations at the source (its own <c>EchoForReason</c>), so for the real
    /// production delegate this is normally a no-op.
    /// </summary>
    static string BoundReason(string reason)
    {
        var stripped = reason.Any(char.IsControl)
            ? new string(reason.Where(c => !char.IsControl(c)).ToArray())
            : reason;
        return stripped.Length <= MaxEchoedReasonChars ? stripped : stripped[..MaxEchoedReasonChars] + "…";
    }

    /// <summary>
    /// Cleans a multi-line ad script completion WITHOUT collapsing its own line structure (PLAN T400
    /// review F1 BLOCKER) — <see cref="LlmCopyWriter.ApplyCopyHygiene"/>'s own contract collapses every
    /// newline to a space (built for a ONE-LINE blurb), so calling it on the whole raw multi-voice reply
    /// destroys the very "TAG: line" shape <c>AdScriptValidator</c> parses: every voice after the first
    /// merges into one giant line, the 1-3-tag/ANNOUNCER-required rules become vacuous (there is only
    /// ever one "tag" left), and the per-line char ceiling becomes a WHOLE-SPOT ceiling (a genuinely
    /// fine 4-line spot fails on total length alone — the reviewer's own repro: a legit 4×220-char spot
    /// collapsed to one 913-char line and failed the 450-char per-line budget it never actually broke).
    ///
    /// <para>
    /// <b>The fix (the <see cref="CrosstalkScriptParser"/> precedent, its own per-line loop):</b> split
    /// on <c>'\n'</c> FIRST, then run hygiene per line, on the TEXT ONLY — after splitting each line at
    /// its own first colon, exactly as <see cref="CrosstalkScriptParser.TryParseLine"/> already does
    /// before ever calling <see cref="LlmCopyWriter.ApplyCopyHygiene"/>. The tag itself NEVER passes
    /// through hygiene at all, which is also what closes the <see cref="LlmCopyWriter.StripChatPreamble"/>
    /// hazard this same review round raised: that heuristic strips everything up to and including an
    /// early colon whose preamble matches a small word list ("sure", "okay", "copy", …) — a tag like
    /// <c>SURE:</c> or <c>OKAY:</c>, if it ever reached hygiene still attached to its own colon, would
    /// silently vanish, mistaken for a chat preamble, and take the whole line's TAG with it. Splitting
    /// the tag off with a plain string slice BEFORE hygiene ever runs on anything means hygiene never
    /// sees a tag-shaped colon to misread as one — the hazard is closed by construction, not by
    /// widening or narrowing the preamble word list.
    /// </para>
    ///
    /// <para>
    /// A line with NO colon at all (already malformed — nothing tag-shaped to protect) runs hygiene on
    /// the whole line instead, exactly as the single-line case always did. This method deliberately does
    /// NOT attempt a SEPARATE whole-response preamble strip of its own for a bare leading chat-preamble
    /// sentence a model occasionally prepends before its first real line — running the SAME word-list
    /// heuristic against arbitrary uppercase TAG values is precisely the unsafe shape this remarks block
    /// just closed, so that class of stray line is left as an ordinary non-tag line for
    /// <c>AdScriptValidator</c>'s own Format rule (and SPEC F160.3's one re-ask) to catch and let the
    /// model self-correct — the SAME ladder this writer already has for every other shape mistake,
    /// rather than a second, riskier mechanism grafted on here.
    /// </para>
    ///
    /// <para>
    /// Blank interior lines are dropped (the <c>AdScriptParser.Parse</c>/<c>CrosstalkScriptParser.Parse</c>
    /// precedent: accidental double-spacing between beats is a formatting quirk, never a shape
    /// violation). Every line's TEXT also has its own stage directions stripped, ANYWHERE in the text
    /// and not only inside the tag (SPEC F201.1, STORY-468): a <c>(parenthetical)</c>, a
    /// <c>[bracketed]</c> beat, or an <c>*asterisked*</c> aside — each required to carry at least one
    /// LETTER, so a sponsor's own <c>(406) 222-0100</c>-shaped phone number never loses its area code
    /// to this pass — is removed whole, BEFORE <see cref="LlmCopyWriter.ApplyCopyHygiene"/> ever runs
    /// on what remains, so a multi-word aside like <c>*long pause*</c> never survives as spoken words
    /// the way a bare emphasis-mark strip alone would leave it. A line where a shape actually matched
    /// has its surrounding whitespace collapsed and a space left dangling before trailing punctuation
    /// tidied away; a line where nothing matched is returned byte-for-byte untouched, so this pass never
    /// rewrites legitimate copy that merely contains an ellipsis, a deliberately spaced colon, or a
    /// stray space near punctuation of its own (PLAN T552 review F1).
    /// </para>
    ///
    /// <para>
    /// A line whose text is STILL empty once continuation-joining (above) has had its own chance to
    /// fill it is dropped WHOLE — before the "nobody is ANNOUNCER" election below ever runs, so an
    /// emptied line can never cast a vote for its own tag (F201.1, PLAN T552 review F2) — and never
    /// surfaced as a bare <c>TAG:</c> the way it was before STORY-468. <c>AdScriptValidator</c>'s own
    /// stage-direction rule (SPEC F201.2) is the backstop that NAMES any of the same three shapes a
    /// script still carries after this pass, never this writer silently forwarding an empty line for the
    /// validator to explain.
    /// </para>
    ///
    /// <para>
    /// <b>The first-contact widening (gh-#696, 2026-09-05).</b> The reference station's own model
    /// (<c>llama3.2:3b</c>, HARDWARE.md) passed the raw format rule on 29% of completions in a 24-run
    /// bench, and every failure was one of a handful of SHAPE quirks the re-ask ladder above was paying
    /// a whole second completion (and, on a CPU box, its whole budget) to correct: the tag wrapped in
    /// quotes (the prompt's own <c>"TAG: &lt;line&gt;"</c> placeholder copied verbatim — see
    /// <see cref="AdScriptPromptBuilder"/>), a mixed-case or spaced or apostrophed tag
    /// (<c>Announcer</c>, <c>VOICE 1</c>, <c>PRUETT'S</c>), a stage direction inside the tag
    /// (<c>LARRY (YELLING)</c>), a beat label used as the speaker (<c>HOOK:</c>, <c>TAGLINE:</c>), a
    /// tag on one line with its words on the next, a title or bracketed direction line with no speaker
    /// at all, and a cast that never used the required ANNOUNCER tag. Each is normalised here,
    /// deterministically, BEFORE the (still pure, still fail-closed) validator ever sees the script:
    /// wrapping quotes/bullets/emphasis are stripped from the whole line (<see cref="FoldTag"/> then
    /// keeps only <c>[A-Za-z0-9]</c> of the tag, upper-cased, parentheticals dropped — never the
    /// chat-preamble heuristic, which the paragraph above rules out for tags); a beat-label tag becomes
    /// <see cref="AdScriptPromptBuilder.AnnouncerTag"/>; an untagged line joins the previous voice's
    /// text (or fills its bare tag); a leading untagged line, a <c>[bracketed]</c>/<c>(parenthesised)</c>
    /// whole-line direction, and a <c>#</c> header are dropped; and when NO line is tagged ANNOUNCER,
    /// the most frequent voice (first on ties) IS the announcer — for a generated spot every tag maps
    /// to the station voice anyway (<c>AdRenderService</c> with a null voice plan), so nothing audible
    /// changes. Owner-typed scripts never pass through here (SPEC F160.4: verbatim, validator at save),
    /// so the widening is confined to the one path that needed it.
    /// </para>
    /// </summary>
    internal static string ApplyLineAwareHygiene(string raw)
    {
        var lines = new List<(string Tag, string Text)>();

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = LineDecorationPattern().Replace(rawLine.Trim(), string.Empty).Trim(LineWrapperChars);
            if (line.Length == 0 || IsWholeLineDirectionOrHeader(line))
                continue;

            var colonIndex = line.IndexOf(':');
            var tagRaw = colonIndex > 0 ? line[..colonIndex] : string.Empty;
            var text = colonIndex > 0 ? line[(colonIndex + 1)..] : line;

            var tag = FoldTag(tagRaw);
            if (colonIndex > 0 && (tag.Length == 0 || tagRaw.Length > MaxRawTagChars))
            {
                // A colon that is not a tag — prose with a colon inside it, or a decorative prefix that
                // folded to nothing — is spoken text with no speaker of its own.
                tag = string.Empty;
                text = line;
            }

            var cleanedText = LlmCopyWriter.ApplyCopyHygiene(StripStageDirections(text));
            if (tag.Length == 0)
            {
                // No speaker: continuation prose joins the previous voice's line (filling a bare tag);
                // a leading line with nobody to join — a title, a chat preamble — is dropped.
                if (lines.Count > 0 && cleanedText.Length > 0)
                    lines[^1] = (lines[^1].Tag, $"{lines[^1].Text} {cleanedText}".Trim());
                continue;
            }

            lines.Add((tag, cleanedText));
        }

        // F201.1 — a line whose text is STILL empty once continuation-joining above has had its own
        // chance to fill it is dropped WHOLE here, BEFORE the "nobody is ANNOUNCER" election below
        // reads lines.Count/lines.GroupBy: an emptied line (e.g. a line that was pure stage direction)
        // must never cast a vote for its own tag, and must never be surfaced as a bare "TAG:" (STORY-468
        // AC5, PLAN T552 review F2).
        lines.RemoveAll(l => l.Text.Length == 0);

        if (lines.Count > 0 && lines.TrueForAll(l => l.Tag != AdScriptPromptBuilder.AnnouncerTag))
        {
            var lead = lines
                .GroupBy(l => l.Tag, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => lines.FindIndex(l => l.Tag == g.Key))
                .First().Key;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Tag == lead)
                    lines[i] = (AdScriptPromptBuilder.AnnouncerTag, lines[i].Text);
            }
        }

        // Emptied lines are already gone (removed above, before the election). A script that empties
        // entirely falls out of this Join as string.Empty — AdScriptValidator's existing "the script
        // has no lines" refusal (AdScriptParser.Parse) handles that case.
        return string.Join('\n', lines.Select(l => $"{l.Tag}: {l.Text}"));
    }

    /// <summary>
    /// SPEC F201.1, STORY-468 — strips a <c>(parenthetical)</c>, a <c>[bracketed]</c> beat, and an
    /// <c>*asterisked*</c> aside ANYWHERE in a line's text (not only when the shape wraps the whole
    /// line), collapses the whitespace the removal leaves behind, and tidies a space stranded before
    /// trailing punctuation. Run BEFORE <see cref="LlmCopyWriter.ApplyCopyHygiene"/> so a multi-word
    /// aside like <c>*long pause*</c> is removed whole — <see cref="LlmCopyWriter.ApplyCopyHygiene"/>'s
    /// own asterisk strip only catches a SINGLE-word run, then falls back to stripping the bare
    /// <c>*</c>/<c>_</c> marks and leaving the words themselves spoken.
    ///
    /// <para>
    /// Each shape must carry at least one LETTER to count (gh-#706 first-contact finding): a stage
    /// direction is always a word or words, never a bare digit run — <see cref="PhoneShape.Regex"/>'s
    /// own <c>(ddd) ddd-dddd</c> alternative (SPEC F197.1) means a sponsor's own area code can arrive
    /// wrapped in real parentheses (<c>"(406) 222-0100"</c>), and that grouping must survive THIS pass
    /// untouched for <see cref="ApplyPhoneHygiene"/> (run after this method) to ever see it. The letter
    /// class (<c>[A-Za-z]</c>) is ASCII-only by design (PLAN T552 review N4): a shape whose only "letters" are non-ASCII
    /// (an accented word, a non-Latin script) carries no <c>[A-Za-z]</c> character and so is left alone by
    /// this pass.
    /// </para>
    /// </summary>
    static string StripStageDirections(string text)
    {
        var stripped = StageDirectionParentheticalPattern().Replace(text, string.Empty);
        stripped = StageDirectionBracketPattern().Replace(stripped, string.Empty);
        stripped = StageDirectionAsteriskPattern().Replace(stripped, string.Empty);

        // PLAN T552 review F1: the shapes above only ever REMOVE characters, so an unchanged length means
        // no shape matched — return the ORIGINAL text untouched rather than running the collapse/tidy
        // passes below, which exist solely to repair the gap a real removal leaves behind. Running them
        // unconditionally rewrote legitimate copy that never had a stage direction in it at all: an
        // ellipsis ("wait ... then go") collapsed to "wait... then go", and a colon/period with
        // deliberate spacing ("Remember : call now", "3 . 5 dollars") lost its spacing.
        if (stripped.Length == text.Length)
            return text;

        stripped = CollapseStrippedGapPattern().Replace(stripped, " ").Trim();
        return SpaceBeforePunctuationPattern().Replace(stripped, "$1");
    }

    [GeneratedRegex(@"\([^()\n]*[A-Za-z][^()\n]*\)")]
    private static partial Regex StageDirectionParentheticalPattern();

    [GeneratedRegex(@"\[[^\[\]\n]*[A-Za-z][^\[\]\n]*\]")]
    private static partial Regex StageDirectionBracketPattern();

    [GeneratedRegex(@"\*[^*\n]*[A-Za-z][^*\n]*\*")]
    private static partial Regex StageDirectionAsteriskPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseStrippedGapPattern();

    /// <summary>A stripped shape can leave a lone space stranded just before the punctuation that
    /// followed it (<c>"today [beat]."</c> strips to <c>"today ."</c>) — folded back against that
    /// punctuation so the sentence reads <c>"today."</c>, never <c>"today ."</c>.</summary>
    [GeneratedRegex(@"\s+([.,;:!?])")]
    private static partial Regex SpaceBeforePunctuationPattern();

    /// <summary>
    /// SPEC F199.2 hygiene, run AFTER <see cref="ApplyLineAwareHygiene"/> on its already-tagged "TAG:
    /// text" output: a phone-shaped digit run that is not the sponsor's own number is rewritten to the
    /// sponsor's own number verbatim (STORY-466 AC3, F199.4 pinned); a sponsor with no
    /// <paramref name="sponsorPhone"/> on file has the number AND its clause dropped instead (AC5). A
    /// run whose DIGITS already equal the sponsor's own — even when formatted differently (parens vs
    /// dashes) or itself containing "555" — is left exactly as written, its own formatting untouched
    /// (AC4). Never touches a line's TAG, only the text after its colon.
    /// </summary>
    internal static string ApplyPhoneHygiene(string script, string? sponsorPhone)
    {
        var trimmedPhone = string.IsNullOrWhiteSpace(sponsorPhone) ? null : sponsorPhone.Trim();
        var phoneDigits = trimmedPhone is null ? null : DigitsOnly(trimmedPhone);

        var lines = new List<string>();
        foreach (var line in script.Split('\n'))
        {
            var hygienic = ApplyPhoneHygieneToLine(line, trimmedPhone, phoneDigits);
            if (hygienic is not null)
                lines.Add(hygienic);
        }

        return string.Join('\n', lines);
    }

    /// <summary>Splits "TAG: text" at its first colon (the exact shape <see cref="ApplyLineAwareHygiene"/>
    /// always produces) and hygienes only the text half. A line whose text hygiene empties entirely
    /// (AC5's "the clause goes" when nothing is left) is dropped — the whole line, tag included — by
    /// returning <see langword="null"/>; a line with no text to begin with (a bare "TAG:") is returned
    /// unchanged, never dropped, since emptiness there predates this pass.</summary>
    static string? ApplyPhoneHygieneToLine(string line, string? sponsorPhone, string? sponsorDigits)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex < 0)
            return line; // never produced by ApplyLineAwareHygiene, but never mangled if it happens

        var tag = line[..colonIndex];
        var body = line[(colonIndex + 1)..];
        var text = body.Length > 0 && body[0] == ' ' ? body[1..] : body;
        if (text.Length == 0)
            return line;

        var cleanedText = ApplyPhoneHygieneToText(text, sponsorPhone, sponsorDigits);
        return cleanedText.Length == 0 ? null : $"{tag}: {cleanedText}";
    }

    static string ApplyPhoneHygieneToText(string text, string? sponsorPhone, string? sponsorDigits)
    {
        var current = text;
        var scanFrom = 0;

        while (true)
        {
            var match = PhoneShapedRunPattern().Match(current, scanFrom);
            if (!match.Success)
                return current;

            var runDigits = DigitsOnly(match.Value);
            if (sponsorDigits is not null && string.Equals(runDigits, sponsorDigits, StringComparison.Ordinal))
            {
                // AC4 — the sponsor's own number (even one that itself contains "555"), unchanged;
                // advance past it so the next search never re-matches the same run.
                scanFrom = match.Index + match.Length;
                continue;
            }

            if (sponsorPhone is not null)
            {
                // AC3, F199.4 pinned — replace with the sponsor's own number verbatim. Resume scanning
                // just PAST the inserted replacement rather than resetting to 0: the text before it is
                // already resolved, and there is nothing left to verify about what we just wrote.
                current = ReplacePhoneRun(current, match, sponsorPhone);
                scanFrom = match.Index + sponsorPhone.Length;
                continue;
            }

            // AC5 — no phone on file: the run and its clause are dropped, never replaced with anything
            // phone-shaped, so a rescan from 0 can never re-trigger on our own output the way the
            // replace branch above could.
            current = DropPhoneClause(current, match.Index, match.Index + match.Length);
            scanFrom = 0;
        }
    }

    static string ReplacePhoneRun(string text, Match match, string replacement) =>
        string.Concat(text.AsSpan(0, match.Index), replacement, text.AsSpan(match.Index + match.Length));

    /// <summary>Deletes from the nearest preceding clause boundary through the matched run. The boundary
    /// char itself (one of <see cref="ClauseBoundaryChars"/>) goes too — it only led into the dropped
    /// clause (<c>"Cravin's Diner, 555-0142."</c> → <c>"Cravin's Diner."</c>). Any leading run of
    /// boundary punctuation left on the remainder is stripped, so a line that was nothing but the phone
    /// clause drops to <see cref="string.Empty"/> rather than a bare "." (AC5); whitespace is collapsed
    /// and, when the deletion reached the line start, the new first letter is capitalized (the F199.4
    /// pin). <see cref="ApplyPhoneHygieneToLine"/> reads an empty result as "drop the whole line".</summary>
    static string DropPhoneClause(string text, int matchStart, int matchEnd)
    {
        var boundary = 0;
        for (var i = matchStart - 1; i >= 0; i--)
        {
            if (Array.IndexOf(ClauseBoundaryChars, text[i]) < 0)
                continue;
            boundary = i;
            break;
        }

        var joined = LeadingClauseBoundaryPattern().Replace(text[..boundary] + text[matchEnd..], string.Empty);
        var remaining = CollapseWhitespacePattern().Replace(joined, " ").Trim();
        if (remaining.Length == 0)
            return string.Empty;

        return boundary == 0 ? char.ToUpperInvariant(remaining[0]) + remaining[1..] : remaining;
    }

    /// <summary>Punctuation that ends a clause (never a phone-run separator itself — those are
    /// narrowly <c>-</c>/<c>.</c>/space inside <see cref="PhoneShapedRunPattern"/>'s own digit groups).
    /// </summary>
    static readonly char[] ClauseBoundaryChars = ['.', ',', ';', ':', '!', '?'];

    static string DigitsOnly(string text) => new(text.Where(char.IsAsciiDigit).ToArray());

    // NANP-shaped digit runs only (optional area code, then a 3-4 local grouping) — deliberately NOT
    // a bare "555-\d{4}" match: the sponsor's own phone (e.g. "812-555-0199") contains "555-0199" as a
    // substring, and matching only that tail would rewrite it to "812-812-555-0199". The optional
    // area-code alternative is tried FIRST (.NET's default greedy order), which is what makes a full
    // "812-555-0199" match as ONE run instead of splitting off its own "555-0199" tail.
    //
    // \b sits AFTER the optional leading paren, not before it (the PhoneShapeCheck.FindViolation
    // precedent, GenWave.Ads — its own remarks explain the same fix): a paren is itself a non-word
    // character, so a \b placed before it never finds a word/non-word transition when the paren is
    // actually present (space-then-paren is non-word-to-non-word) — a leading \b there would make the
    // paren alternative unreachable, and every "(NNN) NNN-NNNN" run would match only its own trailing
    // "NNN-NNNN" tail.
    [GeneratedRegex(@"(?:\(\d{3}\)\s?|\b\d{3}[-. ])?\b\d{3}[-. ]\d{4}\b")]
    private static partial Regex PhoneShapedRunPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespacePattern();

    /// <summary>A leading run of <see cref="ClauseBoundaryChars"/> and/or whitespace — literally those
    /// six characters, kept in sync by hand since <c>GeneratedRegex</c> needs a compile-time constant
    /// and cannot read the array. What <see cref="DropPhoneClause"/> strips from the very front of its
    /// own joined remainder, so an orphaned separator (or a bare terminal "." with nothing left to
    /// terminate) never survives as the whole "sentence".</summary>
    [GeneratedRegex(@"^[.,;:!?\s]+")]
    private static partial Regex LeadingClauseBoundaryPattern();

    /// <summary>A raw tag longer than this is a sentence with a colon in it, never a voice.</summary>
    const int MaxRawTagChars = 40;

    /// <summary>Quote marks, asterisks, and underscores a model wraps a whole line in — trimmed from
    /// both ends BEFORE the tag is split off, so a quoted <c>"TAG: line"</c> yields the tag, not
    /// <c>"TAG</c>.</summary>
    static readonly char[] LineWrapperChars = ['"', '\u201C', '\u201D', '*', '_', ' '];

    /// <summary>Beat labels (<see cref="AdScriptPromptBuilder.Beats"/>, folded) plus the placeholder
    /// words a model lifts from the prompt — as a SPEAKER they mean "the announcer says this".</summary>
    static readonly HashSet<string> BeatLabelTags = new(
        AdScriptPromptBuilder.Beats.Select(FoldTagCharacters).Concat(["TAG", "CTA"]), StringComparer.Ordinal);

    /// <summary>The tag's identity, folded: parentheticals dropped, only letters and digits kept,
    /// upper-cased (<see cref="FoldTagCharacters"/>); a beat label becomes
    /// <see cref="AdScriptPromptBuilder.AnnouncerTag"/>. Never the chat-preamble heuristic (see the
    /// hygiene remarks).</summary>
    static string FoldTag(string tagRaw)
    {
        var folded = FoldTagCharacters(tagRaw);
        return BeatLabelTags.Contains(folded) ? AdScriptPromptBuilder.AnnouncerTag : folded;
    }

    /// <summary>The character fold alone — split out so <see cref="BeatLabelTags"/>' own initialiser can
    /// use it without consulting itself.</summary>
    static string FoldTagCharacters(string tagRaw) =>
        TagNonAlphanumericPattern().Replace(TagParentheticalPattern().Replace(tagRaw, string.Empty), string.Empty)
            .ToUpperInvariant();

    static bool IsWholeLineDirectionOrHeader(string line) =>
        (line[0] == '[' && line[^1] == ']') || (line[0] == '(' && line[^1] == ')') || line[0] == '#';

    [GeneratedRegex(@"^(?:[-*\u2022]\s+|\d+[.)]\s+)")]
    private static partial Regex LineDecorationPattern();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex TagParentheticalPattern();

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex TagNonAlphanumericPattern();

    long ElapsedMs(DateTimeOffset startedAt) => (long)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
}
