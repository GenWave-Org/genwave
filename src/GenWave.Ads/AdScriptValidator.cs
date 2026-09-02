using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads;

/// <summary>
/// Pure, fail-closed, first-rule-wins validation of an ad script (SPEC F160.3, STORY-390) — runs on
/// EVERY path a script reaches the air from: the LLM writer (T400), the owner editor's save (T403),
/// and a catalog pack's install preview (T405). Five checks, in this fixed order, the first violation
/// wins:
///
/// <list type="number">
/// <item><b>Format</b> (<see cref="AdScriptParser"/>) — <c>TAG: line</c>, 1-3 distinct voice tags,
/// ANNOUNCER required, per-line <c>Llm:MaxCopyChars</c>.</item>
/// <item><b>Duration</b> — estimated total read time against <c>spot_seconds</c> +
/// tolerance.</item>
/// <item><b>Brand collision</b> — the shipped, folded blocklist.</item>
/// <item><b>Phone shape</b> — a phone-shaped digit run without 555, checked per line.</item>
/// <item><b>Audience posture</b> — the shipped profanity list, <c>everyone</c> posture only.</item>
/// </list>
///
/// <para>
/// <b>Pure by construction:</b> every live value (posture, per-line char ceiling, spot length,
/// tolerance, the duration estimator) arrives as an ARGUMENT — see <see
/// cref="AdScriptValidationRequest"/>'s own remarks — never read from injected live options. The same
/// (rawScript, request, durationEstimator) triple always produces the same result.
/// </para>
///
/// <para>
/// <b>Duration is text-driven, not estimator-driven</b> (PLAN T399 review F1 — corrects this class's
/// own earlier remarks): the naive design called <see cref="IPatterDurationEstimator.Estimate"/> per
/// line and summed the answers, trusting the seam completely. That is wrong for
/// <see cref="SegmentKind.Ad"/> specifically — the real <c>RollingPatterDurationEstimator</c>'s
/// heuristic tier answers a FIXED "typical copy length" duration for this kind, regardless of the
/// actual line text (nothing observes a rendered ad's duration back into it yet), so the estimate
/// reduced to <c>lineCount × constant</c> — text-BLIND. <see cref="CheckDuration"/> instead computes
/// <c>Σ(line.Text.Length) / CharsPerSecond</c> (the house rate — the SAME constant
/// <c>CrosstalkScriptParser.CharsPerSecond</c>/<c>RollingPatterDurationEstimator</c>'s own cold tier
/// use) as the PRIMARY, always-trusted term. The estimator seam is consulted per line only to WIDEN
/// that estimate, and only when it reports a tier grounded in a REAL measurement (Historical/Exact —
/// some caller already fed a rendered duration back via <c>ObserveRendered</c> for this exact voice
/// tag); its untested Heuristic answer is ignored outright, so a constant-stub estimator can never
/// make this rule text-blind again.
/// </para>
/// </summary>
public static class AdScriptValidator
{
    /// <summary>The house spoken-rate constant (chars/second) — see <see cref="CheckDuration"/>'s own
    /// remarks and this class's own "duration is text-driven" summary above.</summary>
    const double CharsPerSecond = 15.0;

    public static AdScriptValidationResult Validate(
        string rawScript, AdScriptValidationRequest request, IPatterDurationEstimator durationEstimator)
    {
        ArgumentNullException.ThrowIfNull(rawScript);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(durationEstimator);

        var parsed = AdScriptParser.Parse(rawScript, request.MaxLineChars);
        if (parsed is not AdScriptValidationResult.Accepted(var script))
            return parsed;

        if (CheckDuration(script, request, durationEstimator) is { } durationViolation)
            return Refused(durationViolation);

        // Every candidate folding of the script's own text, computed once (PLAN T399 review N3) and
        // shared by both the brand and posture checks below.
        var foldedVariants = AdCopyFold.FoldVariants(JoinLineText(script));

        if (CheckBrandCollision(foldedVariants) is { } brandViolation)
            return Refused(brandViolation);

        if (CheckPhoneShape(script) is { } phoneViolation)
            return Refused(phoneViolation);

        if (request.Posture == AudiencePosture.Everyone &&
            CheckAudiencePosture(foldedVariants) is { } postureViolation)
        {
            return Refused(postureViolation);
        }

        return new AdScriptValidationResult.Accepted(script);
    }

    static string JoinLineText(AdScript script) => string.Join(' ', script.Lines.Select(line => line.Text));

    static AdScriptValidationResult.Refused Refused(AdScriptViolation violation) => new(violation);

    static AdScriptViolation? CheckDuration(
        AdScript script, AdScriptValidationRequest request, IPatterDurationEstimator durationEstimator)
    {
        var totalSeconds = 0.0;
        foreach (var line in script.Lines)
        {
            var textSeconds = line.Text.Length / CharsPerSecond;
            var estimate = durationEstimator.Estimate(SegmentKind.Ad, line.Tag, line.Tag);

            // The text estimate is the floor for every line; a tier the estimator itself flags as
            // grounded in a real observation may only push a line's estimate UP, never down (PLAN
            // T399 review F1) — an untested Heuristic answer never overrides the text-based floor.
            totalSeconds += estimate.Confidence == PatterEstimateConfidence.Heuristic
                ? textSeconds
                : Math.Max(textSeconds, estimate.Duration.TotalSeconds);
        }

        // SPEC F160.3's literal rule is "refuse over" only — an under-length script is never refused
        // here, even though the tolerance is framed as "±" (the ratified spec text wins over the
        // broader "±" framing).
        var ceilingSeconds = request.SpotSeconds * (1 + request.ToleranceRatio);
        if (totalSeconds <= ceilingSeconds)
            return null;

        return new AdScriptViolation(
            AdScriptRuleIds.Duration,
            $"estimated {totalSeconds:F1}s exceeds the {request.SpotSeconds}s target " +
            $"(+{request.ToleranceRatio:P0} tolerance, {ceilingSeconds:F1}s ceiling)");
    }

    static AdScriptViolation? CheckBrandCollision(IReadOnlyList<string> foldedVariants)
    {
        if (FoldedWordListMatcher.FirstMatch(foldedVariants, AdBrandBlocklist.FoldedEntries) is not { } brand)
            return null;

        return new AdScriptViolation(AdScriptRuleIds.BrandCollision, $"the script named a blocklisted brand (\"{brand}\")");
    }

    static AdScriptViolation? CheckPhoneShape(AdScript script)
    {
        // Checked per line, never a whole-script joined string (PLAN T399 review N8) — a digit
        // fragment ending one voice's line must never combine with a fragment opening the next
        // line's into a phone-shaped run that existed in neither line alone.
        foreach (var line in script.Lines)
        {
            if (PhoneShapeCheck.FindViolation(line.Text) is { } phoneRun)
                return new AdScriptViolation(AdScriptRuleIds.PhoneShape, $"a phone-shaped digit run (\"{phoneRun}\") does not contain 555");
        }

        return null;
    }

    static AdScriptViolation? CheckAudiencePosture(IReadOnlyList<string> foldedVariants)
    {
        if (FoldedWordListMatcher.FirstMatch(foldedVariants, AdProfanityList.FoldedEntries) is null)
            return null;

        // Never echoes the matched word itself into the reason — the reason is logged/surfaced
        // (STORY-390 AC9's 400), and repeating it back buys nothing an operator needs.
        return new AdScriptViolation(AdScriptRuleIds.AudiencePosture, "the script contains a profane word under the 'everyone' posture");
    }
}
