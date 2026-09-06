// STORY-405 — L10 knows the new pack tables (SPEC F170.3 · PLAN T428)

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureL10RootListKnowsThePackTables
{
    public sealed class ScenarioTheLawEnumeratesEveryPackTable
    {
        [Fact]
        public void L10ListsStationJinglePack()
            => Assert.Fail("pending: T428 L10 pin — AC3");

        [Fact]
        public void L10ListsStationVoicePack()
            => Assert.Fail("pending: T428 L10 pin — AC3");

        [Fact]
        public void L10ListsStationVoicePackVoice()
            => Assert.Fail("pending: T428 L10 pin — AC3");
    }

    public sealed class ScenarioNoLawViolationAppears
    {
        [Fact]
        public void TheL1ThroughL10SuiteRunsGreenAfterThePin()
            => Assert.Fail("pending: T428 architecture suite green — AC3");
    }
}
