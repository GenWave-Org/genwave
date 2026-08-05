// STORY-279 — The catalog admits the font kind (SPEC F104.1 · PLAN T193/T194)
// Pending: /build-loop turns these green. Feature: the catalog admits the font kind.
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureFontKindAssets
{
    public sealed class ScenarioTheEntryModelCarriesAssets
    {
        [Fact(Skip = "pending T193 (STORY-279 AC1)")]
        public void AFontEntryWithManifestMetaAndAssetsIsAdmittedWithItsAssetReferencesIntact() { }
    }

    public sealed class ScenarioAssetsStreamThroughTheGuardedDoor
    {
        [Fact(Skip = "pending T194 (STORY-279 AC2)")]
        public void AWoff2AssetFetchesThroughTheProxyWithSizeCapAndSha256Applied() { }

        [Fact(Skip = "pending T194 (STORY-279 AC2)")]
        public void AHashMismatchedAssetIsWithheldWithTheIntegrityPosture() { }
    }

    public sealed class ScenarioGoldenParityFixtures
    {
        [Fact(Skip = "pending T193 (STORY-279 AC3)")]
        public void GoldenFontJsonRoundTripsByteStable() { }

        [Fact(Skip = "pending T193 (STORY-279 AC3)")]
        public void TheGoldenWoff2FixtureHashesToItsRecordedSha256() { }
    }

    public sealed class ScenarioOlderAppsSkipFontEntries
    {
        [Fact(Skip = "pending T193 (STORY-279 AC4)")]
        public void AnIndexCarryingAFontEntryStillServesEveryOtherEntry() { }
    }
}
