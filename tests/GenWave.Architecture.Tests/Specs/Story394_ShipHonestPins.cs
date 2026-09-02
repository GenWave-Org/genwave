// STORY-394 — Ship it honest: posture, laws, settings (F157.3/.4, F163 · pending T394/T406)
// AC4 (release + demo) is manual (Dean) — no spec; it closes on his word (T408/T409).

using GenWave.Architecture.Tests.Support;

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
        [Fact]
        public void L5SeedsIncludeGenWaveHostPlugins()
        {
            // HostReservedNamespaces gains the row; the loader is born outside Host (F157.4).
            var entry = Assert.Single(HostReservedNamespaces.Entries, e => e.ReservedNamespace == "GenWave.Host.Plugins");
            Assert.Equal("F157.4", entry.RulingReference);

            // And Host itself actually honors the reservation — the plugin-door wiring (PLAN T394)
            // must never land its own logic under that namespace (mirrors
            // Story292_HostTripwire.ScenarioTheMechanismAndTheSeed.TodaysHostPassesWithTheSeededReservations,
            // re-run here with the grown seed so a future PR that lands plugin-door glue in
            // GenWave.Host.Plugins fails THIS fact, not just the general one).
            var violations = HostNamespaceTripwire.FindViolations(
                ProductionAssemblies.Host.GetTypes(), HostReservedNamespaces.Entries);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
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
