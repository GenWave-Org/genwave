// STORY-426 — Plain-sentence scripts are accepted (SPEC F174.6 · PLAN T444)

namespace GenWave.Ads.Tests.Specs;

public static class FeaturePlainSentenceScriptsAreAccepted
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioUntaggedLinesBecomeAnnouncer
    {
        [Fact]
        public void ThreeUntaggedLinesBecomeThreeAnnouncerLines()
            => Assert.Fail("pending: T444 AdScriptParser untagged → ANNOUNCER — AC1");

        [Fact]
        public void TheTextsAndOrderArePreserved()
            => Assert.Fail("pending: T444 AdScriptParser texts — AC1");
    }

    public sealed class ScenarioTheWritersOwnOutputStaysFullyTagged
    {
        [Fact]
        public void EveryWriterLineStartsWithAValidTag()
            => Assert.Fail("pending: T444 F160.2 grammar — AC4");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRefusingMixedAndStillValidating
    {
        [Fact]
        public void MixedTaggedAndUntaggedRefusesWithFormat()
            => Assert.Fail("pending: T444 \"line 2 has no voice tag while others do\" — AC2");

        [Fact]
        public void APlainSentenceScriptWithANon555NumberRefusesOnThe555Rule()
            => Assert.Fail("pending: T444 F160.3 unchanged after tagging — AC3");
    }
}
