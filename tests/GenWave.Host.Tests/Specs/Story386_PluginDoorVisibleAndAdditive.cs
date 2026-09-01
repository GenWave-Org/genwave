// STORY-386 — What loaded is visible, and additive by construction (F156.1/.5/.7, F157.2 · pending T394)

namespace GenWave.Host.Tests.Specs;

public static class FeaturePluginDoorVisibleAndAdditive
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — through the production composition (WebApplicationFactory)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAPluginProviderJoinsTheFanOut
    {
        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void ThePluginContextProviderResolvesAlongsideWeatherAndHistory()
        {
            // Factory with a plugins root containing one emitted context-provider plugin +
            //   Plugins:Enabled=true: IEnumerable<IContextProvider> contains the plugin's key.
            Assert.Fail("pending T394");
        }
    }

    public sealed class ScenarioStatusReportsEveryOutcome
    {
        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void PluginsArrayCarriesTheLoadedPluginRow()
        {
            // GET /api/status → plugins[] has {name, version, contracts, state:"loaded"}.
            Assert.Fail("pending T394");
        }

        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void PluginsArrayCarriesTheSkippedPluginRowWithReason()
        {
            Assert.Fail("pending T394");
        }

        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void BootWritesOneBoothLogNarrativeRowPerPluginOutcome()
        {
            Assert.Fail("pending T394");
        }
    }

    public sealed class ScenarioPluginSettingsReadTheirOwnSection
    {
        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void SettingReturnsTheConfiguredValueFromPluginsName()
        {
            // Config Plugins:demo:Greeting=hello; host.Setting("Greeting") inside plugin "demo"
            //   returns "hello" (the Context:{Key}:* generic-read precedent — F157.2).
            Assert.Fail("pending T394");
        }

        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void SettingReturnsNullForAMissingKey()
        {
            Assert.Fail("pending T394");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the closed door
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheClosedDoorIsInert
    {
        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void OneKnobAloneLoadsNothingAndSaysWhichHalfIsMissing()
        {
            // Plugins:Enabled=true with no root (and the inverse): zero loads, one INFO naming
            //   the missing half (F156.1).
            Assert.Fail("pending T394");
        }

        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void StatusPluginsIsAnEmptyArrayWhenDisabled()
        {
            Assert.Fail("pending T394");
        }

        [Fact(Skip = "Pending T394 — see docs/PLAN.md")]
        public void TheSeamsCompositionIsByteIdenticalWithTheDoorClosed()
        {
            // The SEAMS generator composition with Plugins:Enabled unset registers nothing new —
            //   the regenerated index matches the checked-in one (F156.8).
            Assert.Fail("pending T394");
        }
    }
}
