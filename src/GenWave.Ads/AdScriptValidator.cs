using System.Text.RegularExpressions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads;

/// <summary>
/// Pure, fail-closed, first-rule-wins validation of an ad script (SPEC F160.3, STORY-390) — runs on
/// EVERY path a script reaches the air from: the LLM writer (T400), the owner editor's save (T403),
/// and a catalog pack's install preview (T405). Six checks, in this fixed order, the first violation
/// wins:
///
/// <list type="number">
/// <item><b>Format</b> (<see cref="AdScriptParser"/>) — <c>TAG: line</c>, 1-3 distinct voice tags,
/// ANNOUNCER required, per-line <c>Llm:MaxCopyChars</c>.</item>
/// <item><b>Stage direction</b> — a parenthetical/bracketed/asterisked aside that survived hygiene
/// (SPEC F201.2, STORY-468) — a shape rule, checked immediately after Format and before Duration.</item>
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
///
/// <para>
/// <b>Owner sponsors are real; pack sponsors stay parody</b> (SPEC F172.5): <see
/// cref="CheckBrandCollision"/> and <see cref="CheckPhoneShape"/> both read the full <see
/// cref="AdScriptValidationRequest"/> now — not just the folded text or the parsed script — because an
/// owner sponsor (<see cref="AdScriptValidationRequest.IsPackOwned"/> <see langword="false"/>) gets
/// its OWN literal name/phone treated as allowed text: the name is stripped from the folded script
/// before the blocklist match runs, and a phone-shaped run whose digits equal the sponsor's own phone
/// digits clears the 555 rule. Nothing else about either check moves — a near-miss name still matches
/// whatever it matches, every other non-555 number still refuses — and a pack-owned sponsor never gets
/// either skip regardless of what its name/phone fields carry. (PLAN T438 ruling: STORY-417's own rule
/// ids <c>phone_not_fictional</c>/<c>brand_blocklisted</c> name this SAME pair of shipped checks — <see
/// cref="AdScriptRuleIds.PhoneShape"/>/<see cref="AdScriptRuleIds.BrandCollision"/> stay exactly as
/// Story390 pinned them on the wire; only the STORY's own vocabulary maps onto them, nothing here was
/// renamed.)
/// </para>
/// </summary>
public static partial class AdScriptValidator
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

        // A shape rule, run right after the parse (SPEC F201.2, STORY-468) — the backstop for the
        // three stage-direction shapes AdScriptWriter.ApplyLineAwareHygiene already strips from an
        // LLM-authored script (GenWave.Tts): an owner-typed save, a pack script, or a hygiene bug
        // never silently airs one of these instead of being named here.
        if (CheckStageDirection(script) is { } stageDirectionViolation)
            return Refused(stageDirectionViolation);

        if (CheckDuration(script, request, durationEstimator) is { } durationViolation)
            return Refused(durationViolation);

        // Every candidate folding of the script's own text, computed once (PLAN T399 review N3) and
        // shared by both the brand and posture checks below.
        var foldedVariants = AdCopyFold.FoldVariants(JoinLineText(script));

        if (CheckBrandCollision(foldedVariants, request) is { } brandViolation)
            return Refused(brandViolation);

        if (CheckPhoneShape(script, request) is { } phoneViolation)
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

    static AdScriptViolation? CheckBrandCollision(IReadOnlyList<string> foldedVariants, AdScriptValidationRequest request)
    {
        // The owner-sponsor name skip (SPEC F172.5, PLAN T438 ruling): a pack-owned sponsor, or an
        // owner sponsor with no name on file, checks the variants unchanged — everything below is
        // identical to before this member existed.
        var variants = request is { IsPackOwned: false, SponsorName: { } sponsorName } && !string.IsNullOrWhiteSpace(sponsorName)
            ? StripLiteral(foldedVariants, AdCopyFold.Fold(sponsorName))
            : foldedVariants;

        if (FoldedWordListMatcher.FirstMatch(variants, AdBrandBlocklist.FoldedEntries) is not { } brand)
            return null;

        return new AdScriptViolation(AdScriptRuleIds.BrandCollision, $"the script named a blocklisted brand (\"{brand}\")");
    }

    /// <summary>Removes every word-boundary occurrence of <paramref name="foldedLiteral"/> from EACH
    /// variant (PLAN T438 ruling: an exact-phrase strip, never a fuzzy per-word match — a near-miss
    /// name sharing only a word with the literal is left untouched, so it still matches whatever
    /// blocklist entry it matches).
    ///
    /// <para>
    /// <b>Each occurrence is replaced with TWO spaces, and the pass repeats until the string stops
    /// changing</b> (PLAN T438 ruling): <see cref="string.Replace(string, string, StringComparison)"/>
    /// is non-overlapping — a single-space replacement of one occurrence would consume the pad space the
    /// NEXT occurrence needs to be found at all, so two occurrences separated by nothing but punctuation
    /// or a line break (adjacent once folded) would leave the SECOND one sitting in the output unstripped.
    /// Replacing with two spaces instead leaves a fresh single-space boundary behind for a repeat pass to
    /// find, so looping to a fixed point strips every occurrence, however many are adjacent.
    /// </para>
    ///
    /// <para>
    /// <b>The resulting double spaces are NEVER collapsed back to one</b> (PLAN T438 ruling): <see
    /// cref="FoldedWordListMatcher"/> is a padded-single-space <see
    /// cref="string.Contains(string, StringComparison)"/> search, so a run of two-plus spaces is itself a
    /// word boundary no <c>" entry "</c> pattern can bridge. Collapsing them back to one space would glue
    /// whatever sat on either side of the stripped literal into a single run — <c>"Mountain " + "Universal
    /// Pictures" + " Dew"</c> stripped down to <c>"mountain dew"</c> would fabricate a collision against
    /// that unrelated blocklist entry; left as <c>"mountain  dew"</c> (two spaces), it cannot.
    /// </para>
    /// </summary>
    static IReadOnlyList<string> StripLiteral(IReadOnlyList<string> foldedVariants, string foldedLiteral) =>
        foldedVariants.Select(variant => StripAllOccurrences(variant, foldedLiteral)).ToList();

    static string StripAllOccurrences(string variant, string foldedLiteral)
    {
        var needle = $" {foldedLiteral} ";
        var padded = $" {variant} ";

        string next;
        while (!string.Equals(next = padded.Replace(needle, "  ", StringComparison.Ordinal), padded, StringComparison.Ordinal))
            padded = next;

        return padded.Trim();
    }

    static AdScriptViolation? CheckPhoneShape(AdScript script, AdScriptValidationRequest request)
    {
        // The owner-sponsor phone skip (SPEC F172.5, PLAN T438 ruling): the sponsor's OWN raw phone
        // number, handed to PhoneShapeCheck.FindViolation exactly as stored — that method owns the ONE
        // digit-normalization point both sides of the comparison go through (PLAN T438 ruling; this
        // class no longer digitizes it here). A pack-owned sponsor, or an owner sponsor with no phone
        // on file, passes null through — the plain 555 rule with no exemption.
        var allowedPhone = request.IsPackOwned ? null : request.SponsorPhone;

        // The reason's own wording tracks which rule actually ran (SPEC F199.3, PLAN T552 ruling): with
        // a real sponsor phone on file the "contains 555" framing went false the moment F199.3 tightened
        // the skip to ONLY that exact number (STORY-466 AC6) — a differing run that itself contains 555
        // still refuses, so telling the operator it "does not contain 555" would be a lie.
        var hasAllowedPhone = !string.IsNullOrWhiteSpace(allowedPhone);

        // Checked per line, never a whole-script joined string (PLAN T399 review N8) — a digit
        // fragment ending one voice's line must never combine with a fragment opening the next
        // line's into a phone-shaped run that existed in neither line alone.
        foreach (var line in script.Lines)
        {
            if (PhoneShapeCheck.FindViolation(line.Text, allowedPhone) is not { } phoneRun)
                continue;

            var reason = hasAllowedPhone
                ? $"a phone-shaped digit run (\"{phoneRun}\") is not the sponsor's own number"
                : $"a phone-shaped digit run (\"{phoneRun}\") does not contain 555";
            return new AdScriptViolation(AdScriptRuleIds.PhoneShape, reason);
        }

        return null;
    }

    /// <summary>SPEC F201.2, STORY-468 AC6 — the backstop for the three shapes
    /// <c>AdScriptWriter.ApplyLineAwareHygiene</c> (GenWave.Tts) already strips from an LLM-authored
    /// script's text: a script that reaches this validator by ANY other path (owner-typed save, a pack
    /// install, a hygiene bug) still refuses if a parenthetical, bracketed beat, or asterisked aside
    /// survives in its spoken text. Checked per line, first match wins, naming both the 1-based line
    /// number and a bounded echo of the offending run.</summary>
    static AdScriptViolation? CheckStageDirection(AdScript script)
    {
        for (var i = 0; i < script.Lines.Count; i++)
        {
            var match = StageDirectionResiduePattern().Match(script.Lines[i].Text);
            if (!match.Success)
                continue;

            return new AdScriptViolation(
                AdScriptRuleIds.StageDirection,
                $"line {i + 1} still carries a stage direction (\"{AdScriptEcho.ForReason(match.Value)}\")");
        }

        return null;
    }

    /// <summary>The SAME three shapes <c>AdScriptWriter.ApplyLineAwareHygiene</c> (GenWave.Tts) strips
    /// before an LLM-authored script's text ever reaches this validator — duplicated here as a literal
    /// pattern because of the L10 boundary (GenWave.Tts cannot reference GenWave.Ads, and Ads does not
    /// reference Tts); a Core-level shared shape is the seam if the two ever drift.
    ///
    /// <para>
    /// Each shape must carry at least one LETTER to count (gh-#706 first-contact finding): a stage
    /// direction is always a word or words, never a bare digit run — <see cref="GenWave.Core.PhoneShape.Regex"/>'s
    /// own <c>(ddd) ddd-dddd</c> alternative (SPEC F197.1) means a sponsor's own area code can arrive
    /// wrapped in real parentheses (<c>"(406) 222-0100"</c>), and that grouping must never trip this
    /// check. The letter class (<c>[A-Za-z]</c>) is ASCII-only by design (PLAN T552 review N4): a shape whose only
    /// "letters" are non-ASCII (an accented word, a non-Latin script) carries no <c>[A-Za-z]</c>
    /// character and so is left alone by this check.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"\([^()\n]*[A-Za-z][^()\n]*\)|\[[^\[\]\n]*[A-Za-z][^\[\]\n]*\]|\*[^*\n]*[A-Za-z][^*\n]*\*")]
    private static partial Regex StageDirectionResiduePattern();

    static AdScriptViolation? CheckAudiencePosture(IReadOnlyList<string> foldedVariants)
    {
        if (FoldedWordListMatcher.FirstMatch(foldedVariants, AdProfanityList.FoldedEntries) is null)
            return null;

        // Never echoes the matched word itself into the reason — the reason is logged/surfaced
        // (STORY-390 AC9's 400), and repeating it back buys nothing an operator needs.
        return new AdScriptViolation(AdScriptRuleIds.AudiencePosture, "the script contains a profane word under the 'everyone' posture");
    }
}
