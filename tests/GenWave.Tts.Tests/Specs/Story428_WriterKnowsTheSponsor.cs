// STORY-428 — The writer knows the sponsor (SPEC F174.8 · PLAN T443)

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheWriterKnowsTheSponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioRequestCarriesTheSponsorFields
    {
        [Fact]
        public void TheRecordCarriesTheSixNewFieldsVerbatim()
            => Assert.Fail("pending: T443 AdScriptWriteRequest Tagline/About/Phone/Address/Website/HouseTone — AC1");
    }

    public sealed class ScenarioPresentFactsAppearOnceInThePrompt
    {
        [Fact]
        public void TheSponsorLineAppearsExactlyOnce()
            => Assert.Fail("pending: T443 BuildUserContent \"Sponsor:\" — AC2");

        [Fact]
        public void TheTaglineAppearsExactlyOnce()
            => Assert.Fail("pending: T443 BuildUserContent tagline — AC2");

        [Fact]
        public void ThePhoneAppearsExactlyOnce()
            => Assert.Fail("pending: T443 BuildUserContent phone — AC2");
    }

    public sealed class ScenarioAbsentFactsDoNotAppear
    {
        [Fact]
        public void ANullWebsiteProducesNoWebsiteLine()
            => Assert.Fail("pending: T443 BuildUserContent absent fact — AC3");
    }

    public sealed class ScenarioABriefsToneOverridesHouseTone
    {
        [Fact]
        public void TheToneLineReadsTheBriefsTone()
            => Assert.Fail("pending: T443 tone override — AC4");

        [Fact]
        public void TheHouseToneDoesNotAppear()
            => Assert.Fail("pending: T443 tone override — AC4");
    }
}
