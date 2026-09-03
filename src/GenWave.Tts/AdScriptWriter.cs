namespace GenWave.Tts;

using System.Diagnostics;
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
/// now, with no caller yet, would be speculative. ONE <c>timeoutCts</c> spans BOTH attempts (mirrors
/// <see cref="LlmCopyWriter.PostChatCompletionAsync"/>'s own re-ask callers) — a re-ask shares this
/// render's existing <c>Llm:TimeoutSeconds</c> budget, never a fresh one.
/// </para>
///
/// <para>
/// Registered as a DI singleton with NO eager I/O in its constructor (Story125's zero-I/O invariant,
/// the same posture <see cref="CrosstalkScriptWriter"/>'s own remarks document) — every dependency
/// here is itself a cheap seam, so constructing this class never touches the network.
/// </para>
/// </summary>
public sealed class AdScriptWriter(
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
                "Llm:Endpoint is not configured", ruleId: null, LlmCallCause.ConnectionFailure, request.Brand,
                timeProvider.GetUtcNow(), mode, systemPrompt: null, userPrompt: null, cfg.Model);
        }

        var systemPrompt = AdScriptPromptBuilder.BuildSystemPrompt(request);
        var userPrompt = AdScriptPromptBuilder.BuildUserContent(request);

        // ONE timeout budget spans BOTH attempts (mirrors LlmCopyWriter.PostChatCompletionAsync's own
        // re-ask callers, its own remarks) — a re-ask shares whatever this render's Llm:TimeoutSeconds
        // budget has left, never a fresh one. The client/URI/generation cap are likewise resolved ONCE:
        // both attempts target the same endpoint with the same cap.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));
        var http = httpClientFactory.CreateClient(LlmCopyWriter.HttpClientName);
        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");
        var maxTokens = DeriveScriptGenerationCap(request);

        var firstAttempt = await AttemptAsync(
            http, requestUri, cfg, request, systemPrompt, userPrompt, maxTokens, validate, mode, ct, timeoutCts.Token);
        if (firstAttempt is not AdScriptAttemptOutcome.ValidatorRefused refused)
            return ResultOf(firstAttempt); // Success, or a transport/generation fault — never re-asked (skip-only).

        // SPEC F160.3's ladder shape: exactly ONE re-ask, naming the violated rule, appended to the
        // SAME user prompt the rejected draft already saw.
        var reaskUserPrompt = userPrompt + "\n\n" + AdScriptPromptBuilder.BuildReaskLine(refused.RuleId, refused.Reason);
        var secondAttempt = await AttemptAsync(
            http, requestUri, cfg, request, systemPrompt, reaskUserPrompt, maxTokens, validate, mode, ct, timeoutCts.Token);
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
                    ruleId: null, LlmCallCause.OverLength, request.Brand, startedAt, mode, systemPrompt, userPrompt,
                    cfg.Model, reply.Content));
            }

            var cleaned = ApplyLineAwareHygiene(reply.Content);
            if (cleaned.Length == 0)
            {
                return Resolved(Failed(
                    "the completion was empty after cleanup", ruleId: null, LlmCallCause.EmptyCompletion,
                    request.Brand, startedAt, mode, systemPrompt, userPrompt, cfg.Model, reply.Content));
            }

            return validate(cleaned) switch
            {
                AdScriptValidationOutcome.Accepted => Resolved(
                    Accept(cleaned, request.Brand, systemPrompt, userPrompt, reply.Content, startedAt, mode, cfg.Model)),
                AdScriptValidationOutcome.Refused refused => BuildRefused(
                    refused, request.Brand, startedAt, mode, systemPrompt, userPrompt, cfg.Model, reply.Content),
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
                request.Brand, systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode, cause, cfg.Model, LlmCallKind.AdScript);
            logger.LogInformation(
                "Ad script failed (brand: {Brand}): {Detail}",
                request.Brand.ReplaceLineEndings(" "), detail.ReplaceLineEndings(" "));
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
    /// The five rule id tokens are <c>GenWave.Ads.AdScriptRuleIds</c>' own wire vocabulary, duplicated
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
    /// violation). A line whose text is empty after hygiene keeps its own bare <c>TAG:</c> (never
    /// silently dropped whole) — so <c>AdScriptValidator</c> reports the honest, specific "the {tag}
    /// line has no spoken text" reason rather than a misleading "no {tag} line appeared" for a line that
    /// DID arrive, just empty.
    /// </para>
    /// </summary>
    static string ApplyLineAwareHygiene(string raw)
    {
        var lines = new List<string>();

        foreach (var rawLine in raw.Split('\n'))
        {
            var trimmedLine = rawLine.Trim();
            if (trimmedLine.Length == 0)
                continue;

            var colonIndex = trimmedLine.IndexOf(':');
            if (colonIndex <= 0)
            {
                // No tag-shaped prefix on this line at all — nothing for hygiene to accidentally eat,
                // so the whole line runs through the ordinary single-line hygiene pass, unchanged.
                var cleanedWhole = LlmCopyWriter.ApplyCopyHygiene(trimmedLine);
                if (cleanedWhole.Length > 0)
                    lines.Add(cleanedWhole);
                continue;
            }

            var tag = trimmedLine[..colonIndex].Trim();
            var cleanedText = LlmCopyWriter.ApplyCopyHygiene(trimmedLine[(colonIndex + 1)..]);
            lines.Add(cleanedText.Length == 0 ? $"{tag}:" : $"{tag}: {cleanedText}");
        }

        return string.Join('\n', lines);
    }

    long ElapsedMs(DateTimeOffset startedAt) => (long)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
}
