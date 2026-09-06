// STORY-405 — The settings split holds: Ads:* Live, Packs:* env/compose-only (SPEC F170.1/F170.2 · PLAN T417)

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdsSettingsAreLiveAndPacksSettingsAreEnvOnly
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — live keys accept via allowlist
    // ---------------------------------------------------------------------

    public sealed class ScenarioLiveAllowlistAcceptsAdsKeys
    {
        [Fact]
        public void AdsAnnouncerVoiceIsAcceptedByThePutSettingsEndpoint()
            => Assert.Fail("pending: T417 allowlist — AC4");

        [Fact]
        public void AdsCastVoicesIsAcceptedByThePutSettingsEndpoint()
            => Assert.Fail("pending: T417 allowlist — AC4");

        [Fact]
        public void AdsBedFadeMsIsAcceptedByThePutSettingsEndpoint()
            => Assert.Fail("pending: T417 allowlist — AC4");

        [Fact]
        public void AdsBedFadeMsIsValidatedInTheHundredToOneThousandRange()
            => Assert.Fail("pending: T417 range validation — AC4");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — env-only keys refuse via allowlist
    // ---------------------------------------------------------------------

    public sealed class ScenarioEnvOnlyKeysRefuseViaAllowlist
    {
        [Fact]
        public void PacksJingleRootIsRefusedByThePutSettingsEndpoint()
            => Assert.Fail("pending: T417 env-only fence — AC4");

        [Fact]
        public void PacksVoicesRootIsRefusedByThePutSettingsEndpoint()
            => Assert.Fail("pending: T417 env-only fence — AC4");

        [Fact]
        public void PacksPreviewMaxBytesIsRefusedByThePutSettingsEndpoint()
            => Assert.Fail("pending: T417 env-only fence — AC4");

        [Fact]
        public void PacksJingleAssetMaxBytesIsRefusedByThePutSettingsEndpoint()
            => Assert.Fail("pending: T417 env-only fence — AC4");
    }
}
