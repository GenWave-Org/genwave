// STORY-364 — The station's own name is never a lie (SPEC F138.8 · PLAN T350, wire T352)
//
// BDD specification — xUnit. PENDING: every Specification is skipped until T350 builds the
// behavior (the compile-clean-pending convention). /build-loop unskips per task; a body still
// failing after its task is a defect, not a pending.
//
// The 2026-08-28 exhibit is the pinned regression: announcement #1's re-ask carried the owner's
// sentence intact and was killed by the station name alone ("unsupported claim: 108.8") — the
// announcement lane runs CheckFacts against a fact block that is the owner's message, so the
// station's own digits read as an invented number. F138.8: the name's spans join the exempt
// spans the track title already gets, on EVERY lane. The deployed-entry-point scenario (the
// production preview endpoint through a scripted completions stub) lives in GenWave.Host.Tests
// (Story364_TheGateRulesOnThePreviewWire.cs, T352) — this file pins the pure CopyClaims seam.
namespace GenWave.Tts.Tests.Specs;

using GenWave.Tts;
using Xunit;

public static class FeatureStationNameIsNeverALie
{
    // ── Shared arrange — the exhibit, verbatim ─────────────────────────────────────────────
    const string StationName = "GWAV 108.8";
    const string OwnerMessage = "Dinner is ready — come and get it while it's hot.";
    const string ReaskCopy =
        "Well, rockers! This one's hot off the grill for ya! Dinner is ready — come and get it " +
        "while it's HOT! GWAV 108.8... Keep those fists pumping!";
    const string ContextFactBlock = "overcast · 15°C";

    // ---------------------------------------------------------------------
    // HAPPY PATH — the name is a supported fact wherever it appears
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheExhibitPasses
    {
        // Given the AC1 station and a fact block holding only the owner's message,
        // When CopyClaims.CheckFacts checks ReaskCopy with the station name as an exempt span.
        [Fact]
        public void NoDigitRunViolationIsRaisedForTheStationsFrequency()
        {
            var result = CopyClaims.CheckFacts(ReaskCopy, OwnerMessage, StationName);

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "108.8");
        }
    }

    public sealed class ScenarioTheNameAppearsTwice
    {
        // When the copy names the station mid-sentence and again at the end.
        [Fact]
        public void NeitherOccurrenceRaisesADigitRunViolation()
        {
            const string copy = "GWAV 108.8, keeping the hits coming — that's right, GWAV 108.8!";

            var result = CopyClaims.CheckFacts(copy, OwnerMessage, StationName);

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "108.8");
        }
    }

    public sealed class ScenarioTheNamesWordsAreExemptFromTheClockCheck
    {
        // Given "Saturday Night Radio" on a station-local Tuesday,
        // When CopyClaims.CheckClock checks "It's Saturday Night Radio, glad you're here".
        [Fact]
        public void NoWeekdayViolationIsRaisedForSaturday()
        {
            const string stationName = "Saturday Night Radio";
            const string copy = "It's Saturday Night Radio, glad you're here.";
            var tuesday = new DateTimeOffset(2026, 8, 18, 11, 0, 0, TimeSpan.Zero);

            var result = CopyClaims.CheckClock(copy, tuesday, stationName: stationName);

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "Saturday");
        }
    }

    public sealed class ScenarioTheNamesOwnConditionWordIsExemptFromTheFactsCheck
    {
        // Given the station "Sunny 101.5" and a fact block that never mentions sunshine (HIGH-1
        // review finding: the condition-word loop had NO exemption at all before T350's fix),
        // When CopyClaims.CheckFacts checks "You're on Sunny 101.5."
        [Fact]
        public void NoConditionWordViolationIsRaisedForSunny()
        {
            const string stationName = "Sunny 101.5";
            const string factBlock = "overcast · 15°C";
            const string copy = "You're on Sunny 101.5.";

            var result = CopyClaims.CheckFacts(copy, factBlock, stationName);

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.ConditionWord && v.Token == "Sunny");
        }
    }

    public sealed class ScenarioASecondNamesOwnConditionWordIsAlsoExempt
    {
        // Given the station "Storm FM" and a fact block that never mentions a storm,
        // When CopyClaims.CheckFacts checks "You're locked into Storm FM."
        [Fact]
        public void NoConditionWordViolationIsRaisedForStorm()
        {
            const string stationName = "Storm FM";
            const string factBlock = "clear skies";
            const string copy = "You're locked into Storm FM.";

            var result = CopyClaims.CheckFacts(copy, factBlock, stationName);

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.ConditionWord && v.Token == "Storm");
        }
    }

    public sealed class ScenarioTheNamesWordsAreExemptFromTheFactsCheckToo
    {
        // Given "Saturday Night Radio" and a fact block that never mentions Saturday (MEDIUM-1
        // review finding: only CheckClock's own weekday exemption was pinned before T350's fix —
        // CheckFacts's own weekday exemption, mutating nameSpans to [] left all fourteen facts
        // green, was never independently proven),
        // When CopyClaims.CheckFacts checks "It's Saturday Night Radio, playing all your favorites."
        [Fact]
        public void NoWeekdayViolationIsRaisedForSaturday()
        {
            const string stationName = "Saturday Night Radio";
            const string factBlock = "clear, 60°F";
            const string copy = "It's Saturday Night Radio, playing all your favorites.";

            var result = CopyClaims.CheckFacts(copy, factBlock, stationName);

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "Saturday");
        }
    }

    public sealed class ScenarioAnUntrimmedStationNameStillExempts
    {
        // Given a station name stored with a leading and trailing space (MEDIUM-2 review finding,
        // the 2026-08-28 bug shape restored by matching the raw, unnormalized name),
        // When the copy names the station cleanly (no padding).
        [Fact]
        public void NoDigitRunViolationIsRaisedForTheFrequency()
        {
            const string copy = "GWAV 108.8, coming at you live!";

            var result = CopyClaims.CheckFacts(copy, OwnerMessage, stationName: " GWAV 108.8 ");

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "108.8");
        }
    }

    public sealed class ScenarioADoubleSpacedStationNameStillExempts
    {
        // Given a station name stored with a doubled internal space (MEDIUM-2 review finding),
        // When the copy names the station cleanly (single-spaced, post-hygiene shape).
        [Fact]
        public void NoDigitRunViolationIsRaisedForTheFrequency()
        {
            const string copy = "GWAV 108.8, coming at you live!";

            var result = CopyClaims.CheckFacts(copy, OwnerMessage, stationName: "GWAV  108.8");

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "108.8");
        }
    }

    public sealed class ScenarioCrosstalkKeepsTheStationsOwnNameOffTheTruthGate
    {
        // Given a well-formed HOST/NEIGHBOR script that names the station "Sunny 101.5" by name
        // (LOW-3 review finding: CrosstalkScriptParser's own TruthShapeChecks table ran with no
        // station-name exemption at all — a station named "Sunny 101.5" tripped its own
        // ConditionWord shape on "Sunny" every time the booth said its own name),
        // When CrosstalkScriptParser.Parse checks it with that same station name threaded through.
        [Fact]
        public void TheScriptIsAcceptedNotDiscarded()
        {
            const string stationName = "Sunny 101.5";
            const string raw =
                "HOST: You're locked into Sunny 101.5, glad you could join us today.\n" +
                "NEIGHBOR: Glad to be here, always a good time on this show.\n" +
                "HOST: Stick around, more good times are coming right up.";
            var stationLocalNow = new DateTimeOffset(2026, 8, 18, 15, 0, 0, TimeSpan.Zero);

            var result = CrosstalkScriptParser.Parse(
                raw, maxLineChars: 200, durationTargetSeconds: 60, stationLocalNow, stationName);

            Assert.IsType<CrosstalkWriteResult.Accepted>(result);
        }
    }

    public sealed class ScenarioTheContextLaneIsUnchangedForEverythingElse
    {
        // Given ContextFactBlock and the station "GWAV 108.8",
        // When the copy says "a sunny 22 degrees on GWAV 108.8".
        [Fact]
        public void TheViolationsAreExactlyTwentyTwoAndSunny()
        {
            const string copy = "It's a sunny 22 degrees out there on GWAV 108.8.";

            var result = CopyClaims.CheckFacts(copy, ContextFactBlock, StationName);

            Assert.Equal(
                [
                    new ClaimViolation(ClaimClass.DigitRun, "22"),
                    new ClaimViolation(ClaimClass.ConditionWord, "sunny"),
                ],
                result.Violations);
        }
    }

    public sealed class ScenarioTheLeadInLaneKeepsPassingTheName
    {
        // Given the station "Sunday Drive Radio" on a station-local Tuesday, with no fact block
        // (a LeadIn render never has one — CheckFacts never runs, only CheckClock does), and a
        // copy that carries NO marker of its own (L2 review finding, PLAN T350: the prior copy,
        // "Keep it locked right now on GWAV 108.8.", matched no weekday/daypart marker at all, so
        // the station-name exemption was never actually exercised here — the fact stayed green
        // under all thirteen T350 mutations for a reason unrelated to what it claimed to pin).
        // The station's own name here CONTAINS a present-frame weekday marker ("it's Sunday"),
        // so this fact is load-bearing: proven red by temporarily removing the name span from
        // FindTitleSpans's exemption list, which raises a Weekday violation for "Sunday" here.
        // When CopyClaims.CheckClock checks "It's Sunday Drive Radio, keep it locked."
        [Fact]
        public void NoWeekdayViolationIsRaisedForTheNamesOwnSunday()
        {
            const string stationName = "Sunday Drive Radio";
            const string copy = "It's Sunday Drive Radio, keep it locked.";
            var stationLocalTuesday = new DateTimeOffset(2026, 8, 18, 11, 0, 0, TimeSpan.Zero);

            var result = CopyClaims.CheckClock(copy, stationLocalTuesday, stationName: stationName);

            Assert.DoesNotContain(
                result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "Sunday");
        }
    }

    public sealed class ScenarioTheNameMatchIsCaseInsensitive
    {
        // Given the station "SUNNY 101.5" and a fact block that only mentions "overcast" (L3
        // review finding, PLAN T350: FindTitleSpans's own case-insensitive literal match,
        // StringComparison.OrdinalIgnoreCase, was never independently pinned — flipping it to
        // Ordinal redded nothing before this fact existed, since every OTHER fixture in this file
        // spells the copy's mention of the name with the SAME casing as the name itself),
        // When CopyClaims.CheckFacts checks "You're on sunny 101.5…" — the copy's casing of the
        // name differs from the station's own stored casing on both the condition word ("sunny"
        // vs "SUNNY") and, if it mattered here, the digit run share the one exempt span either way.
        [Fact]
        public void NoViolationIsRaisedForTheNamesOwnCaseFoldedMention()
        {
            const string stationName = "SUNNY 101.5";
            const string factBlock = "overcast";
            const string copy = "You're on sunny 101.5…";

            var result = CopyClaims.CheckFacts(copy, factBlock, stationName);

            Assert.Empty(result.Violations);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — what is not the name is still a lie
    // ---------------------------------------------------------------------

    public sealed class ScenarioANumberThatIsNotTheNameStillViolates
    {
        // When the copy says "Dinner is ready at 7 PM — come and get it while it's hot."
        [Fact]
        public void ADigitRunViolationIsRaisedForSeven()
        {
            const string copy = "Dinner is ready at 7 PM — come and get it while it's hot.";

            var result = CopyClaims.CheckFacts(copy, OwnerMessage, StationName);

            Assert.Contains(result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "7");
        }
    }

    public sealed class ScenarioAnEmptyStationNameExemptsNothing
    {
        // Given a blank station name, When the copy carries "108.8" with no supporting fact.
        [Fact]
        public void ADigitRunViolationIsRaisedForTheFrequency()
        {
            const string copy = "GWAV 108.8, coming at you live!";

            var result = CopyClaims.CheckFacts(copy, OwnerMessage, stationName: "");

            Assert.Contains(result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "108.8");
        }
    }

    public sealed class ScenarioAWhitespaceOnlyStationNameExemptsNothing
    {
        // Given a whitespace-only station name (MEDIUM-1 review finding — the blank-name guard's
        // OTHER half: an empty string was already pinned, but " " was not),
        // When the copy carries "108.8" with no supporting fact.
        [Fact]
        public void ADigitRunViolationIsRaisedForTheFrequency()
        {
            const string copy = "GWAV 108.8, coming at you live!";

            var result = CopyClaims.CheckFacts(copy, OwnerMessage, stationName: "   ");

            Assert.Contains(result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "108.8");
        }
    }
}
