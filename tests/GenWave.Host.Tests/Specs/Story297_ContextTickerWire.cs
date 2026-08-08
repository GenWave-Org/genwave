// STORY-297 — Context segments air at boundaries: the Host wire (F107.3, T226)

namespace GenWave.Host.Tests.Specs;

public static class FeatureContextTickerWire
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the deployed entry point (composition root + settings surface)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTickerIsWired
    {
        [Fact(Skip = "Pending T226 — see docs/PLAN.md")]
        public void ContextTickerServiceRegistersAsAHostedService()
        {
            // WebApplicationFactory<Program>: the service collection contains the ticker
            // as IHostedService; it is the ONE wall-clock actor (no second ticker type).
            // Assert.Single(hostedServices, s => s is ContextTickerService);
            Assert.Fail("pending T226");
        }

        [Fact(Skip = "Pending T226 — see docs/PLAN.md")]
        public void ContextSettingsAreAllowlisted()
        {
            // StationSettingsAllowlist.All contains Context:Weather:Enabled,
            // Context:Weather:SegmentCadenceMinutes, Context:Weather:PatterCadenceMinutes,
            // Context:Weather:PersonaId, Context:History:* siblings,
            // Station:Location:Latitude/Longitude/SpokenName,
            // Station:Imaging:ClockAnchoredIdents, Station:Imaging:TimeAnnouncements — all Live.
            // Assert.Superset(expectedKeys, allowlistKeys);
            Assert.Fail("pending T226");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDisabledMeansSilentAndOffline
    {
        [Fact(Skip = "Pending T226 — see docs/PLAN.md")]
        public void WithEverythingDisabledTheTickerMakesNoOutboundCalls()
        {
            // Boot the factory with all Context:*:Enabled false ⇒ zero fetches on the
            // fake handlers, zero deferrals enqueued.
            // Assert.Equal(0, fakeHandler.CallCount);
            Assert.Fail("pending T226");
        }
    }
}
