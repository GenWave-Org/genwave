// STORY-394 — Ship it honest: posture, laws, settings (F157.3/.4, F163 · pending T394/T406)
// AC4 (release + demo) is manual (Dean) — no spec; it closes on his word (T408/T409).

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureShipHonestPins
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePostureIsWrittenDown
    {
        [Fact(Skip = "Pending T406 — see docs/PLAN.md")]
        public void PluginsMdExistsAtTheRepoRoot()
        {
            // The F157.3 statement: MIT to compile, AGPL-compatible to distribute in-proc,
            //   unconstrained out-of-proc, no obligation for private plugins.
            Assert.Fail("pending T406");
        }
    }

    public sealed class ScenarioTheLawsKnowTheNewProjects
    {
        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void L5SeedsIncludeGenWaveHostPlugins()
        {
            // HostReservedNamespaces gains the row; the loader is born outside Host (F157.4).
            Assert.Fail("pending T394");
        }

        [Fact(Skip = "Pending T406 — see docs/PLAN.md")]
        public void L10RootsIncludePluginsAndAds()
        {
            // The cycle-freedom TheoryData gains GenWave.Plugins and GenWave.Ads.
            Assert.Fail("pending T406");
        }

        [Fact(Skip = "Pending T406 — see docs/PLAN.md")]
        public void TheAddHttpClientPinStillReadsThree()
        {
            // Neither the loader nor the ads lane owns HTTP (F157.4/F160.1).
            Assert.Fail("pending T406");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the settings split cannot drift
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSettingsSplitHolds
    {
        [Fact(Skip = "Pending T406 — see docs/PLAN.md")]
        public void TheFiveStationAdsKeysAreLiveWithValidatorsAndHelpText()
        {
            // EveryNUnits/TargetCount/RefreshDays/AutoApprove/AntiRepeatWindow — allowlisted,
            //   validated, three-way help parity (F163.1).
            Assert.Fail("pending T406");
        }

        [Fact(Skip = "Pending T406 — see docs/PLAN.md")]
        public void NoPluginsOrAdsInfraKeyIsAllowlisted()
        {
            // Plugins:* and Ads:* never appear in StationSettingsAllowlist (F156.1/F163.2).
            Assert.Fail("pending T406");
        }
    }
}
