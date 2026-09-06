// STORY-398 — Two voice packs with the same voice id can't coexist (SPEC F166.4 · PLAN T413)

namespace GenWave.Host.Tests.Specs;

public static class FeatureVoicePacksWithTheSameVoiceIdCannotCoexist
{
    // ---------------------------------------------------------------------
    // SAD PATH — the whole feature is refusal
    // ---------------------------------------------------------------------

    public sealed class ScenarioInstalledPackCollisionRefuses
    {
        [Fact]
        public void ASecondPackWithAnAlreadyInstalledVoiceIdReturns409()
            => Assert.Fail("pending: T413 voice-pack collision guard — AC1");

        [Fact]
        public void The409ProblemDetailsListsTheCollidedIds()
            => Assert.Fail("pending: T413 ProblemDetails body — AC1");

        [Fact]
        public void The409ProblemDetailsNamesTheIncumbentPackSlug()
            => Assert.Fail("pending: T413 ProblemDetails body — AC1");
    }

    public sealed class ScenarioStockCollisionRefuses
    {
        [Fact]
        public void APackReUsingAStockVoiceIdReturns409()
            => Assert.Fail("pending: T413 kokoro /v1/audio/voices probe — AC2");

        [Fact]
        public void The409ProblemDetailsNamesStockAsIncumbent()
            => Assert.Fail("pending: T413 ProblemDetails body — AC2");
    }

    public sealed class ScenarioNoBytesOnCollision
    {
        [Fact]
        public void NoBytesAreWrittenUnderTheSecondPacksSlugOnCollision()
            => Assert.Fail("pending: T413 pre-write ordering — AC3");

        [Fact]
        public void StationVoicePackVoiceIsUnchangedOnCollision()
            => Assert.Fail("pending: T413 UNIQUE guard — AC3");
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a rename installs
    // ---------------------------------------------------------------------

    public sealed class ScenarioARenameInstallsCleanly
    {
        [Fact]
        public void BothVoiceIdsAppearInGetApiVoicesAfterASuccessfulRename()
            => Assert.Fail("pending: T413 rename path + T421 wire — AC4");
    }
}
