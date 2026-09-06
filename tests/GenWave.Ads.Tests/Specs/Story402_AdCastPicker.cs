// STORY-402 — Every generated spot rides two distinct voices from the cast pool (SPEC F167 · closes gh-#702 · PLAN T415)

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdCastPickerBuildsAVoicePlan
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSettingsLiveOnTheAllowlist
    {
        [Fact]
        public void AdsAnnouncerVoiceAppearsInTheAllowlistWithTheDocumentedDefault()
            => Assert.Fail("pending: T417 allowlist — AC1");

        [Fact]
        public void AdsCastVoicesAppearsInTheAllowlistSeededWithFourStockIds()
            => Assert.Fail("pending: T417 allowlist + /plan-picked seed — AC1");
    }

    public sealed class ScenarioWorkerBuildsAVoicePlan
    {
        [Fact]
        public void AnnouncerTagMapsToAdsAnnouncerVoice()
            => Assert.Fail("pending: T415 picker — AC2");

        [Fact]
        public void Voice1IsPickedDeterministicallyFromCastVoicesMinusAnnouncer()
            => Assert.Fail("pending: T415 picker — AC2");

        [Fact]
        public void Voice2IsAlsoDeterministicallyPickedAndDistinctFromVoice1()
            => Assert.Fail("pending: T415 picker — AC2");

        [Fact]
        public void VoicePlanIsPersistedOnTheAdSpotRow()
            => Assert.Fail("pending: T415 persistence — AC2");
    }

    public sealed class ScenarioRenderUsesDistinctVoices
    {
        [Fact]
        public void KokoroReceivesAtLeastTwoRequestsWithDistinctVoiceIds()
            => Assert.Fail("pending: T415 + F161.2 wire — AC3 (log-provable)");
    }

    public sealed class ScenarioRegenerationReCastsIdentically
    {
        [Fact]
        public void ARetryReproducesAByteIdenticalVoicePlan()
            => Assert.Fail("pending: T415 determinism seed = hash(spot.id ^ brand) — AC4");
    }

    public sealed class ScenarioAnnouncerExcludedFromCast
    {
        [Fact]
        public void Voice1IsNeverEqualToTheAnnouncerVoice()
            => Assert.Fail("pending: T415 candidate-strip — AC5");

        [Fact]
        public void Voice2IsNeverEqualToTheAnnouncerVoice()
            => Assert.Fail("pending: T415 candidate-strip — AC5");
    }

    public sealed class ScenarioOwnerDraftsKeepTheirExplicitVoicePlan
    {
        [Fact]
        public void TheWorkerDoesNotOverwriteAnExplicitVoicePlanOnAnOwnerDraft()
            => Assert.Fail("pending: T415 owner-override precedence — AC6");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEmptyCastFallsBackHonestly
    {
        [Fact]
        public void EveryTagMapsToTheStationVoiceWhenCastVoicesIsEmpty()
            => Assert.Fail("pending: T415 fallback — AC7");

        [Fact]
        public void AnInfoLogLinePerTickNamesTheEmptyPool()
            => Assert.Fail("pending: T415 rate-limited INFO — AC7");

        [Fact]
        public void TheRenderStillSucceedsInTheFallback()
            => Assert.Fail("pending: T415 fallback + F161.2 wire — AC7");
    }
}
