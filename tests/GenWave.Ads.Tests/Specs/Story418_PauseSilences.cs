// STORY-418 — Pausing a sponsor silences their spots on the very next pick (SPEC F173.1/F173.2 · PLAN T439)

namespace GenWave.Ads.Tests.Specs;

public static class FeaturePausingASponsorSilencesTheirSpotsOnTheVeryNextPick
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheExcludeListAddsPausedSponsorsSpots
    {
        [Fact]
        public void ListAiringExclusionsContainsThePausedSponsorsReadyMedia()
            => Assert.Fail("pending: T439 IAdSpotStore.ListAiringExclusionsAsync — AC1");
    }

    public sealed class ScenarioOnePickAfterPauseIsSilence
    {
        [Fact]
        public void GetNextSpotNeverReturnsThePausedSponsorsMedia()
            => Assert.Fail("pending: T439 LibraryAdSpotSource exclude union — AC2");
    }

    public sealed class ScenarioRefillSkipsPausedSponsorsBriefs
    {
        [Fact]
        public void NoSpotIsCreatedFromAPausedSponsorsBriefs()
            => Assert.Fail("pending: T439 RefillIfNeededAsync joins unpaused — AC3 (T440)");

        [Fact]
        public void NoLlmCompletionIsLogged()
            => Assert.Fail("pending: T439 no spend — AC3 (T440)");
    }

    public sealed class ScenarioResumeRestoresAiringWithinOnePickNoReRender
    {
        [Fact]
        public void TheMediaIsEligibleAgainOnTheNextPick()
            => Assert.Fail("pending: T439 resume — AC4");

        [Fact]
        public void TheSpotStateIsStillReady()
            => Assert.Fail("pending: T439 no transition on resume — AC4");
    }
}
