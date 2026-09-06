// STORY-396 — A voice pack for the wrong engine is refused before bytes hit disk (SPEC F164.2 · PLAN T413)

namespace GenWave.Host.Tests.Specs;

public static class FeatureVoicePackForWrongEngineIsRefusedBeforeBytesHitDisk
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioMatchingEngineInstalls
    {
        [Fact]
        public void KokoroStationAcceptsAKokoroPack()
            => Assert.Fail("pending: T413 engine check — AC1");

        [Fact]
        public void TheVoiceFilesLandOnTheVolumeAsUsual()
            => Assert.Fail("pending: T413 install continues past engine check — AC1");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioMismatchedEngineRefuses
    {
        [Fact]
        public void ThePiperOnlyStationRefusesAKokoroPackWith400()
            => Assert.Fail("pending: T413 not_supported_engine ProblemDetails — AC2");

        [Fact]
        public void TheProblemDetailsTypeNamesTheEngineMismatch()
            => Assert.Fail("pending: T413 ProblemDetails type = not_supported_engine — AC2");

        [Fact]
        public void TheProblemDetailsBodyNamesBothStationAndPackEngines()
            => Assert.Fail("pending: T413 ProblemDetails body — AC2");
    }

    public sealed class ScenarioNoBytesOnRefusal
    {
        [Fact]
        public void NoPtFileAppearsUnderVoicesSlugOnRefusal()
            => Assert.Fail("pending: T413 bytes-after-check ordering — AC3");

        [Fact]
        public void StationVoicePackHasNoRowForTheSlugOnRefusal()
            => Assert.Fail("pending: T413 all-or-nothing install — AC3");
    }

    public sealed class ScenarioShelfEntryUnchanged
    {
        [Fact]
        public void TheShelfStillOffersTheInstallButtonAfterRefusal()
            => Assert.Fail("pending: T413 + T418 — AC4 (no half-install state on the shelf)");
    }
}
