// STORY-365 — My words count as preserved in any case and punctuation (SPEC F144.3 amended · PLAN T350, wire T352)
//
// BDD specification — xUnit. PENDING until T350 (the compile-clean-pending convention).
//
// F144.3 amended (gh-#632): "the core" is the message's WORD SEQUENCE — both message and copy
// are hygiene-normalised, case-folded, and reduced to word tokens (punctuation is not a word),
// and the message's token run must appear contiguous and in order in the copy. Case and
// punctuation are free; words are not. The MEDIUM-B invariant (an all-markup core is a
// violation, never a vacuous pass) stands. Subject: CopyClaims.CheckContainment.
namespace GenWave.Tts.Tests.Specs;

using GenWave.Tts;
using Xunit;

public static class FeatureWordsCountAsPreserved
{
    const string DinnerCore = "Dinner is ready — come and get it while it's hot.";

    // ---------------------------------------------------------------------
    // HAPPY PATH — case and punctuation are free
    // ---------------------------------------------------------------------

    public sealed class ScenarioTerminalPunctuationIsFree
    {
        // When the copy contains "Dinner is ready — come and get it while it's HOT! GWAV 108.8".
        [Fact]
        public void ContainmentPasses()
        {
            const string copy = "Dinner is ready — come and get it while it's HOT! GWAV 108.8";

            var result = CopyClaims.CheckContainment(copy, DinnerCore);

            Assert.True(result.Passed);
        }
    }

    public sealed class ScenarioCaseIsFree
    {
        // Given the core "the garage sale starts at nine",
        // When the copy contains "The Garage Sale Starts At Nine, folks".
        [Fact]
        public void ContainmentPasses()
        {
            const string core = "the garage sale starts at nine";
            const string copy = "The Garage Sale Starts At Nine, folks";

            var result = CopyClaims.CheckContainment(copy, core);

            Assert.True(result.Passed);
        }
    }

    public sealed class ScenarioInteriorPunctuationAndApostrophesAreFree
    {
        // Given the core "Mom's flight lands at 6 - pick her up",
        // When the copy contains "Mom’s flight lands at 6, pick her up" (U+2019, comma for dash).
        [Fact]
        public void ContainmentPasses()
        {
            const string core = "Mom's flight lands at 6 - pick her up";
            const string copy = "Mom’s flight lands at 6, pick her up";

            var result = CopyClaims.CheckContainment(copy, core);

            Assert.True(result.Passed);
        }
    }

    public sealed class ScenarioNonLatinWordsAreWordsToo
    {
        // Given a core written entirely in Greek (HIGH-2 review finding: the review-round-1
        // ASCII-only WordTokenRx tokenized this to ZERO words and hit the empty-core violation
        // unconditionally, regardless of what the copy said — an outright regression against the
        // ORIGINAL literal-substring check this method replaced),
        // When the copy echoes it verbatim.
        [Fact]
        public void ContainmentPasses()
        {
            const string core = "Καλημέρα";
            const string copy = "Καλημέρα, radio family — great to have you with us!";

            var result = CopyClaims.CheckContainment(copy, core);

            Assert.True(result.Passed);
        }
    }

    public sealed class ScenarioAnAccentedWordStaysOneToken
    {
        // Given a core naming an accented Latin word (HIGH-2 review finding: the ASCII-only
        // WordTokenRx split "Café" at its own accented letter, mid-word),
        // When the copy echoes the SAME accented spelling verbatim.
        [Fact]
        public void ContainmentPasses()
        {
            const string core = "Café is now open";
            const string copy = "Café is now open, come on by!";

            var result = CopyClaims.CheckContainment(copy, core);

            Assert.True(result.Passed);
        }
    }

    public sealed class ScenarioTheRunMustBeContiguous
    {
        // When the copy contains "Dinner is ready, rockers, come and get it while it's hot".
        [Fact]
        public void ContainmentFailsWithAnAnnouncementCoreViolation()
        {
            const string copy = "Dinner is ready, rockers, come and get it while it's hot";

            var result = CopyClaims.CheckContainment(copy, DinnerCore);

            var violation = Assert.Single(result.Violations);
            Assert.Equal(ClaimClass.AnnouncementCore, violation.Class);
        }
    }

    public sealed class ScenarioTheRunMustBeInOrder
    {
        // Given the core "come and get it while it's hot",
        // When the copy contains "while it's hot, come and get it".
        [Fact]
        public void ContainmentFailsWithAnAnnouncementCoreViolation()
        {
            const string core = "come and get it while it's hot";
            const string copy = "while it's hot, come and get it";

            var result = CopyClaims.CheckContainment(copy, core);

            var violation = Assert.Single(result.Violations);
            Assert.Equal(ClaimClass.AnnouncementCore, violation.Class);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — words are not free
    // ---------------------------------------------------------------------

    public sealed class ScenarioAParaphraseIsStillAReject
    {
        // The 2026-08-28 attempt shape: "Dinner's ready and steamin' hot, so dig in while it lasts!"
        [Fact]
        public void ContainmentFailsWithAnAnnouncementCoreViolation()
        {
            const string copy = "Dinner's ready and steamin' hot, so dig in while it lasts!";

            var result = CopyClaims.CheckContainment(copy, DinnerCore);

            var violation = Assert.Single(result.Violations);
            Assert.Equal(ClaimClass.AnnouncementCore, violation.Class);
        }
    }

    public sealed class ScenarioAnAccentedWordNeverFalselyMatchesADifferentWord
    {
        // Given the core "über party" (HIGH-2 review finding: the ASCII-only WordTokenRx split
        // "über" down to its own ASCII remainder "ber", so this core read as falsely PRESENT
        // inside "schmüber party" — both split to the SAME shared ASCII tail),
        // When the copy contains "schmüber party" instead — a different word entirely.
        [Fact]
        public void ContainmentFailsWithAnAnnouncementCoreViolation()
        {
            const string core = "über party";
            const string copy = "schmüber party continues all night";

            var result = CopyClaims.CheckContainment(copy, core);

            var violation = Assert.Single(result.Violations);
            Assert.Equal(ClaimClass.AnnouncementCore, violation.Class);
        }
    }

    public sealed class ScenarioAnAllMarkupCoreIsAViolationNeverAVacuousPass
    {
        // Given the core "*urgent*" (hygiene-strips to nothing), When any copy is checked.
        [Fact]
        public void ContainmentFailsWithAnAnnouncementCoreViolation()
        {
            const string core = "*urgent*";
            const string copy = "Stay tuned for more great music coming your way!";

            var result = CopyClaims.CheckContainment(copy, core);

            var violation = Assert.Single(result.Violations);
            Assert.Equal(ClaimClass.AnnouncementCore, violation.Class);
        }
    }
}
