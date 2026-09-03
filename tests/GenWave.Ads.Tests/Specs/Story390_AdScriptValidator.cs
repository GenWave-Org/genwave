// STORY-390 — A validator keeps the ads honest (validator half: AC1/AC4–AC8 · F160.3 · PLAN T399)
// The writer half (AC2/AC3) lives in GenWave.Tts.Tests/Specs/Story390_AdScriptWriter.cs;
// the owner-editor half (AC9) lives in GenWave.Host.Tests/Specs/Story392_AdsApi.cs.

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdScriptValidator
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static readonly AdScriptValidationRequest DefaultRequest = new(
        Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4);

    static AdScriptValidationResult Validate(string script, AdScriptValidationRequest? request = null) =>
        AdScriptValidator.Validate(script, request ?? DefaultRequest, new FakePatterDurationEstimator());

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioACleanScriptPassesWhole
    {
        const string CleanScript =
            "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
            "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
            "ANNOUNCER: Call 555-0142 — that's 555-0142 — Cravin's Diner.";

        [Fact]
        public void AWellFormedThirtySecondScriptPassesWithZeroViolations()
        {
            // ANNOUNCER-led, two voices, within duration tolerance, fictional brand, 555 number.
            var result = Validate(CleanScript);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void ParsedLinesCarryTheirVoiceTags()
        {
            var result = Validate(CleanScript);

            var accepted = Assert.IsType<AdScriptValidationResult.Accepted>(result);
            Assert.Equal(["ANNOUNCER", "VOICE1", "ANNOUNCER"], accepted.Script.Lines.Select(line => line.Tag));
        }

        [Fact]
        public void TheProfanityCheckIsSkippedUnderMaturePosture()
        {
            // The same script refused under 'everyone' passes the posture check under 'mature'.
            const string ProfaneScript =
                "ANNOUNCER: This deal is straight up shit hot, folks.\nANNOUNCER: Call 555-0100 today.";

            var refusedUnderEveryone = Validate(ProfaneScript);
            var passedUnderMature = Validate(ProfaneScript, DefaultRequest with { Posture = AudiencePosture.Mature });

            Assert.IsType<AdScriptValidationResult.Refused>(refusedUnderEveryone);
            Assert.IsType<AdScriptValidationResult.Accepted>(passedUnderMature);
        }

        [Fact]
        public void AFourBeatFifteenSecondStructurePasses()
        {
            // PLAN T399 review F1: T400's planned structure — hook, pitch, tagline, CTA, ANNOUNCER-led
            // — must comfortably clear a 15s slot once duration is computed from the text itself.
            var request = DefaultRequest with { SpotSeconds = 15 };
            var script =
                "ANNOUNCER: Tired of the same old radio ads?\n" +
                "ANNOUNCER: Cravin's Diner flips the script with a deal this good.\n" +
                "ANNOUNCER: Cravin's Diner. Taste the difference.\n" +
                "ANNOUNCER: Call 555-0100 today.";

            var result = Validate(script, request);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — each rule refuses, first-rule-wins, naming its rule id
    // ---------------------------------------------------------------------

    public sealed class ScenarioBrandCollisionRefuses
    {
        [Fact]
        public void ABlocklistedBrandRefusesNamingTheRule()
        {
            var result = Validate("ANNOUNCER: Nothing beats an ice cold Coca Cola on a hot day.\nANNOUNCER: Call 555-0100 today.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }

        [Theory]
        [InlineData("ANNOUNCER: Coca-Cola presents tonight's giveaway.\nANNOUNCER: Call 555-0100 today.")]
        [InlineData("ANNOUNCER: Grab a C0ke while supplies last.\nANNOUNCER: Call 555-0100 today.")]
        [InlineData("ANNOUNCER: Tastes just like c o k e to me.\nANNOUNCER: Call 555-0100 today.")]
        [InlineData("ANNOUNCER: Coka-Cola presents tonight's giveaway.\nANNOUNCER: Call 555-0100 today.")]
        public void CaseSpacingAndLeetVariantsAreCaughtByTheFold(string script)
        {
            // "Coca-Cola presents" (whitespace/hyphen fold), "C0ke" (leet), "c o k e" (letter-spacing
            // merge), and the literal SPEC F160.3 exhibit "Coka-Cola presents" (a near-miss SPELLING —
            // caught only because BrandBlocklist.txt's own dedicated data entry lists it; PLAN T399
            // review F5 — near-miss spellings are curated DATA, never edit-distance fuzzing).
            var result = Validate(script);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }

        [Fact]
        public void ALeadingArticleNextToALetterSpacedBrandStillRefuses()
        {
            // PLAN T399 review F2: a genuine single-letter word ("a") sitting directly against a
            // letter-spaced evasion attempt must not corrupt the merge into a non-matching blob
            // ("acoke") — the drop-leading fold variant catches it.
            var result = Validate("ANNOUNCER: Grab a c o k e for the road.\nANNOUNCER: Call 555-0100 today.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }

        [Fact]
        public void ALeadingArticleNextToACorrectlySpelledMAndMsStillRefuses()
        {
            // PLAN T399 round-2 review R2-A: a genuine TWO-token run ("a" + "M" from "M&M's") was
            // falling through to the whole-merge branch regardless of which fold variant was asked
            // for (the drop only applied when the run was STRICTLY longer than 2) — a correctly
            // spelled brand in completely ordinary copy silently accepted. The >= fix restores the
            // "kept apart" variant for a run of exactly 2.
            var result = Validate("ANNOUNCER: Grab a M&M's.\nANNOUNCER: Call 555-0100 today.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }

        [Fact]
        public void ALeadingArticleNextToACorrectlySpelledFiveHourEnergyStillRefuses()
        {
            // Same R2-A fix, the digit-leading case: the run is "a"+"5" (2 tokens) — the "5 hour
            // energy" entry's own leading "5" token must survive as its own token, not merge into "a5".
            var result = Validate("ANNOUNCER: A 5 hour energy for the road.\nANNOUNCER: Call 555-0100 today.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }
    }

    public sealed class ScenarioPhoneShapeRefuses
    {
        [Fact]
        public void APhoneShapedDigitRunWithout555Refuses()
        {
            var result = Validate("ANNOUNCER: Give us a call at 867-5309 today.\nANNOUNCER: We can't wait to hear from you.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.PhoneShape, refused.Violation.RuleId);
        }

        [Fact]
        public void A555NumberPasses()
        {
            var result = Validate("ANNOUNCER: Give us a call at 555-0134 today.\nANNOUNCER: We can't wait to hear from you.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }
    }

    public sealed class ScenarioAudiencePostureRefuses
    {
        [Fact]
        public void AProfanityUnderEveryonePostureRefuses()
        {
            var result = Validate("ANNOUNCER: This deal is straight up bullshit good.\nANNOUNCER: Call 555-0100 today.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.AudiencePosture, refused.Violation.RuleId);
        }

        [Fact]
        public void TheProfanityListGuardsOnlyAdCopy()
        {
            // Scope pin (F160.3): no non-ad path resolves the ad profanity list — a source-text pin
            // (the cheap honest shape: GenWave.Ads is disjoint from every other project's own
            // dependency graph, so a grep over every OTHER project's source is a complete check).
            var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
            var srcRoot = Path.Combine(repoRoot, "src");
            var adsProjectPrefix = Path.Combine(srcRoot, "GenWave.Ads") + Path.DirectorySeparatorChar;

            var offendingFiles = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(adsProjectPrefix, StringComparison.Ordinal))
                .Where(file => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file), @"\bAdProfanityList\b"))
                .ToList();

            Assert.Empty(offendingFiles);
        }
    }

    public sealed class ScenarioDurationRefuses
    {
        [Fact]
        public void AnOverLongEstimatedReadRefusesNamingTheDurationRule()
        {
            // Four lines whose combined text comfortably exceeds the 30s target's tolerance ceiling
            // once estimated at the house 15 chars/sec rate (PLAN T399 review F1 — duration is
            // TEXT-driven, not line-count-driven): 4 x 180 chars = 720 chars -> 48s, over the 42s
            // ceiling (30s x 1.4). Each line stays under MaxLineChars(200) on its own.
            var filler = new string('x', 180);
            var result = Validate($"ANNOUNCER: {filler}\nANNOUNCER: {filler}\nANNOUNCER: {filler}\nANNOUNCER: {filler}");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Duration, refused.Violation.RuleId);
        }

        [Fact]
        public void TenTimesMoreCharactersAtTheSameLineCountRefusesWhileTheShortScriptDoesNot()
        {
            // PLAN T399 review F1: the duration estimate must scale with TEXT, not just line count —
            // a constant-per-line "stub" estimator (Heuristic confidence, exactly what SegmentKind.Ad's
            // real RollingPatterDurationEstimator answers today) must never make the rule text-blind.
            var request = DefaultRequest with { SpotSeconds = 5, ToleranceRatio = 0.4, MaxLineChars = 500 };
            var stub = new FakePatterDurationEstimator { DurationPerCall = TimeSpan.FromSeconds(2) };

            var shortScript = $"ANNOUNCER: {new string('x', 15)}\nVOICE1: {new string('x', 15)}";
            var longScript = $"ANNOUNCER: {new string('x', 150)}\nVOICE1: {new string('x', 150)}"; // 10x the chars, same 2 lines.

            var shortResult = AdScriptValidator.Validate(shortScript, request, stub);
            var longResult = AdScriptValidator.Validate(longScript, request, stub);

            Assert.IsType<AdScriptValidationResult.Accepted>(shortResult);
            var refused = Assert.IsType<AdScriptValidationResult.Refused>(longResult);
            Assert.Equal(AdScriptRuleIds.Duration, refused.Violation.RuleId);
        }

        [Fact]
        public void AHistoricalEstimateLongerThanTheTextTermWidensTheTotalAndRefuses()
        {
            // PLAN T399 round-2 review R2-B: the estimator seam may WIDEN the text-based estimate when
            // it reports a REAL (Historical/Exact) tier. Text alone: 2 lines x 15 chars = 30 chars ->
            // 2.0s, comfortably under the 7s ceiling (5s x 1.4) — would ACCEPT on text alone. A
            // Historical estimate of 5s/line (x2 = 10s) exceeds that ceiling and must win.
            var request = DefaultRequest with { SpotSeconds = 5, ToleranceRatio = 0.4, MaxLineChars = 500 };
            var historical = new FakePatterDurationEstimator
            {
                Confidence = PatterEstimateConfidence.Historical,
                DurationPerCall = TimeSpan.FromSeconds(5),
            };
            var script = $"ANNOUNCER: {new string('x', 15)}\nVOICE1: {new string('x', 15)}";

            var result = AdScriptValidator.Validate(script, request, historical);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Duration, refused.Violation.RuleId);
        }

        [Fact]
        public void AHistoricalEstimateShorterThanTheTextTermDoesNotShrinkTheTotal()
        {
            // The inverse of the fact above: a Historical estimate SMALLER than the text term must
            // never shrink the total below what the text alone implies — an over-long script (by text
            // alone: 2 lines x 150 chars = 300 chars -> 20s, over the 7s ceiling) still refuses even
            // though the "real" per-line observation (0.1s) is tiny.
            var request = DefaultRequest with { SpotSeconds = 5, ToleranceRatio = 0.4, MaxLineChars = 500 };
            var historical = new FakePatterDurationEstimator
            {
                Confidence = PatterEstimateConfidence.Historical,
                DurationPerCall = TimeSpan.FromSeconds(0.1),
            };
            var filler = new string('x', 150);
            var script = $"ANNOUNCER: {filler}\nVOICE1: {filler}";

            var result = AdScriptValidator.Validate(script, request, historical);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Duration, refused.Violation.RuleId);
        }
    }

    public sealed class ScenarioFormatRefuses
    {
        [Fact]
        public void FourVoiceTagsRefuse()
        {
            var result = Validate("ANNOUNCER: one.\nVOICE1: two.\nVOICE2: three.\nVOICE3: four.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
        }

        [Fact]
        public void AMissingAnnouncerRefuses()
        {
            var result = Validate("VOICE1: one.\nVOICE2: two.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
        }

        [Fact]
        public void ALineOverMaxCopyCharsRefuses()
        {
            // The crosstalk reuse — Llm:MaxCopyChars per line, deliberately not a new knob.
            var result = Validate(
                "ANNOUNCER: This line is definitely longer than twenty characters.",
                DefaultRequest with { MaxLineChars = 20 });

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
        }

        [Fact]
        public void ADigitOnlyTagIsMalformedFormat()
        {
            // PLAN T399 review N4: the tag grammar requires a leading letter — "12" is not a
            // plausible voice name, so the whole line reads as malformed format.
            var result = Validate("12: is the time.\nANNOUNCER: fallback line.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
        }

        [Fact]
        public void ATimeLikeDigitSequenceInsideLineTextIsFine()
        {
            // The N4 tag-grammar tightening only touches the TAG before the first colon — a second
            // colon deeper in a line's own spoken text (e.g. a clock reading) is untouched.
            var result = Validate("ANNOUNCER: It's 12:30, don't miss out!\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void AHundredKilobyteColonLessLineYieldsABoundedSingleLineReason()
        {
            // PLAN T399 review F6: an untrusted raw line echoed into a Reason is truncated (the
            // crosstalk MaxEchoedLineChars=120 precedent) and stripped of control characters
            // (CWE-117 log forging) before it ever reaches the caller.
            var hugeLine = "ANNOUNCER" + '\r' + new string('x', 100_000) + '\a';

            var result = Validate(hugeLine);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
            Assert.True(refused.Violation.Reason.Length < 200, $"Reason was {refused.Violation.Reason.Length} chars");
            Assert.DoesNotContain('\r', refused.Violation.Reason);
            Assert.DoesNotContain('\a', refused.Violation.Reason);
        }
    }

    // ---------------------------------------------------------------------
    // ADDED MUTANT PINS — leet/spacing negatives, data audit negatives, a phone anchoring negative,
    // and first-rule-wins determinism
    // ---------------------------------------------------------------------

    public sealed class ScenarioFoldingNegativesDoNotFalsePositive
    {
        [Fact]
        public void CocoaDoesNotCollideWithTheCocaColaEntry()
        {
            var result = Validate("ANNOUNCER: Warm up with a mug of cocoa tonight.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void ApplesauceDoesNotCollideWithAnyAppleEntry()
        {
            // "apple" is deliberately NOT a bare blocklist entry (BrandBlocklist.txt's own
            // precision-bias header) — only "iphone"/"macbook"/"apple inc" cover the brand, so
            // ordinary words built on "apple" never collide.
            var result = Validate("ANNOUNCER: Grandma's applesauce recipe never gets old.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void PricesYearsAndTwentyFourSevenDoNotTripThePhoneRule()
        {
            var result = Validate(
                "ANNOUNCER: We're open 24/7, all year, with prices starting at $24.99 in 2026.\n" +
                "ANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void ASpacedOutDigitTieDoesNotTripProfanity()
        {
            // PLAN T399 review F2/F3: the leading-article merge bug ("a" + "5" + "5" -> "ass") is
            // fixed both by the drop-variant merge AND by digit-only tokens never leet-substituting.
            var result = Validate("ANNOUNCER: It was a 5 5 tie in the ninth.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void AHyphenatedSpacedDigitTieDoesNotTripProfanity()
        {
            var result = Validate("ANNOUNCER: It's a 5-5 tie tonight, folks.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void GradeARatingWithSpacedDigitsDoesNotTripProfanity()
        {
            // PLAN T399 review F3: leet-substitution only applies inside a token that already
            // contains a real letter — a bare digit token ("5") never becomes "s".
            var result = Validate("ANNOUNCER: Grade A, 5 5 rated service, every time.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void ADollarPriceDoesNotTripAnything()
        {
            var result = Validate("ANNOUNCER: Everything in the store is just $19.99 today.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void HmAsAnInterjectionDoesNotCollideWithTheDroppedHAndMEntry()
        {
            // PLAN T399 review F4: "h&m" was dropped from BrandBlocklist.txt for exactly this reason.
            var result = Validate("ANNOUNCER: Hm, what a deal this is.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void TicTacToeDoesNotCollideWithTheQualifiedTicTacMintsEntry()
        {
            // PLAN T399 review F4: "tic tac" was qualified to "tic tac mints" for exactly this reason.
            var result = Validate("ANNOUNCER: Let's play a game of tic tac toe.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void AZipPlusFourCodeDoesNotTripThePhoneRule()
        {
            // PLAN T399 review N8: the phone regex is now \b-anchored at both ends — "210-1234" is no
            // longer matchable as a bare substring embedded inside the longer "90210-1234" run.
            var result = Validate("ANNOUNCER: Find us at zip 90210-1234, right off Main Street.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void DigitsSplitAcrossTwoLinesNeverSynthesizeAPhoneShapedRun()
        {
            // PLAN T399 review N8: each line is checked independently — joining "123." and "4567"
            // with a space would form a phone-shaped "123 4567", but neither line alone does.
            var result = Validate("ANNOUNCER: Item number 123.\nVOICE1: Yours for only 4567 points.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void SizesSCommaMAndLDoesNotCollideDespiteTheWidenedTwoTokenRunFix()
        {
            // PLAN T399 round-2 review R2-A regression guard: the run is "S"+"M" (2 tokens, same
            // length as the M&M's case above) — but no blocklist/profanity entry is a bare 1- or
            // 2-letter token, so widening the drop-variant threshold to >= never starts flagging
            // ordinary short spelled-out letters.
            var result = Validate("ANNOUNCER: Sizes S, M and L are in stock.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        [Fact]
        public void IOUDoesNotCollideWithAnythingEither()
        {
            var result = Validate("ANNOUNCER: I O U one favor, friend.\nANNOUNCER: Call 555-0100 today.");

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }
    }

    public sealed class ScenarioFirstRuleWinsIsDeterministic
    {
        [Fact]
        public void AFormatAndDurationViolationNamesFormatFirst()
        {
            var filler = new string('x', 180);
            var result = Validate($"ANNOUNCER: {filler}\nVOICE1: {filler}\nVOICE2: {filler}\nVOICE3: {filler}");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
        }

        [Fact]
        public void ADurationAndBrandViolationNamesDurationFirst()
        {
            // Both a duration and a brand-collision violation are present — duration precedes brand
            // in the fixed evaluation order (SPEC F160.3), so it is the one named.
            var filler = new string('x', 180);
            var result = Validate(
                $"ANNOUNCER: {filler}\nANNOUNCER: {filler}\nANNOUNCER: {filler}\nANNOUNCER: {filler}\nANNOUNCER: Grab a Coca Cola.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Duration, refused.Violation.RuleId);
        }

        [Fact]
        public void ABrandAndPhoneViolationNamesBrandFirst()
        {
            var result = Validate("ANNOUNCER: Grab an ice cold Coca Cola and call 867-5309 now.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }

        [Fact]
        public void APhoneAndPostureViolationNamesPhoneFirst()
        {
            var result = Validate("ANNOUNCER: This bullshit deal — call 867-5309 now.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.PhoneShape, refused.Violation.RuleId);
        }

        [Fact]
        public void AFormatAndBrandViolationNamesFormatFirst()
        {
            // Missing ANNOUNCER (format) AND a brand collision — format precedes every other rule.
            var result = Validate("VOICE1: Grab an ice cold Coca Cola right now.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
        }
    }
}
