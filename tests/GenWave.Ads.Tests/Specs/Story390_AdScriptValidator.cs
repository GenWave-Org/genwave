// STORY-390 — A validator keeps the ads honest (validator half: AC1/AC4–AC8 · F160.3 · pending T399)
// The writer half (AC2/AC3) lives in GenWave.Tts.Tests/Specs/Story390_AdScriptWriter.cs;
// the owner-editor half (AC9) lives in GenWave.Host.Tests/Specs/Story392_AdsApi.cs.

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdScriptValidator
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioACleanScriptPassesWhole
    {
        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void AWellFormedThirtySecondScriptPassesWithZeroViolations()
        {
            // ANNOUNCER-led, two voices, within duration tolerance, fictional brand, 555 number.
            Assert.Fail("pending T399");
        }

        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void ParsedLinesCarryTheirVoiceTags()
        {
            Assert.Fail("pending T399");
        }

        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void TheProfanityCheckIsSkippedUnderMaturePosture()
        {
            // The same script refused under 'everyone' passes the posture check under 'mature'.
            Assert.Fail("pending T399");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — each rule refuses, first-rule-wins, naming its rule id
    // ---------------------------------------------------------------------

    public sealed class ScenarioBrandCollisionRefuses
    {
        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void ABlocklistedBrandRefusesNamingTheRule()
        {
            Assert.Fail("pending T399");
        }

        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void CaseSpacingAndLeetVariantsAreCaughtByTheFold()
        {
            // "Coka-Cola", "c o k e", "C0ke" — the folding catches the near-miss spellings.
            Assert.Fail("pending T399");
        }
    }

    public sealed class ScenarioPhoneShapeRefuses
    {
        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void APhoneShapedDigitRunWithout555Refuses()
        {
            Assert.Fail("pending T399");
        }

        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void A555NumberPasses()
        {
            Assert.Fail("pending T399");
        }
    }

    public sealed class ScenarioAudiencePostureRefuses
    {
        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void AProfanityUnderEveryonePostureRefuses()
        {
            Assert.Fail("pending T399");
        }

        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void TheProfanityListGuardsOnlyAdCopy()
        {
            // Scope pin (F160.3): no non-ad path resolves the ad profanity list.
            Assert.Fail("pending T399");
        }
    }

    public sealed class ScenarioDurationRefuses
    {
        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void AnOverLongEstimatedReadRefusesNamingTheDurationRule()
        {
            Assert.Fail("pending T399");
        }
    }

    public sealed class ScenarioFormatRefuses
    {
        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void FourVoiceTagsRefuse()
        {
            Assert.Fail("pending T399");
        }

        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void AMissingAnnouncerRefuses()
        {
            Assert.Fail("pending T399");
        }

        [Fact(Skip = "Pending T399 — see docs/PLAN.md")]
        public void ALineOverMaxCopyCharsRefuses()
        {
            // The crosstalk reuse — Llm:MaxCopyChars per line, deliberately not a new knob.
            Assert.Fail("pending T399");
        }
    }
}
