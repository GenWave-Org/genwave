// STORY-380 — The knobs and the laws: L5 reserves Gardener, thumbs never cross into the other
// ledgers (SPEC F155.2/F155.3 · PLAN T357, T367)
//
// BDD specification — xUnit. PENDING until T357/T367. T357 adds `GenWave.Host.Gardener` to
// `HostReservedNamespaces.Entries` and a probe fixture under `Fixtures/L5Probe/Gardener` (the same
// idiom Story292_HostTripwire.cs's `ReservedHit` fixture already proves against
// `HostNamespaceTripwire.FindViolations`) — this file only pins the fitness-suite-level fact that
// the seeded reservation reds a type placed there; T357 itself builds the fixture and wires it in.
// T367 builds the disjointness pin: a call-graph scan over `ProductionAssemblies.Host` (mirrors
// Story366_AnnounceSchemeFenceTwoCarriers.cs's `AnnounceSchemeFence`, but walking call sites the
// way `MemberCallSiteScan` already does for other laws here, not attribute carriers) proving no
// path from `SpectatorThumbsController` or the booth-log station-thumb action ever reaches
// `MediaRatingRepository` or `PersonaTasteAccrualRepository` — the three petals' write paths stay
// disjoint by construction.
namespace GenWave.Architecture.Tests.Specs;

public static class FeatureGardenerNamespaceAndDisjointness
{
    public sealed class ScenarioL5ReservesTheNamespace
    {
        // Given a type under GenWave.Host.Gardener, When the fitness suite runs.
        [Fact(Skip = "pending T357 (STORY-380 AC3)")]
        public void L5FailsNamingIt() => Assert.Fail("pending T357");
    }

    public sealed class ScenarioThreeWayDisjointness
    {
        // Given the production assemblies, When the disjointness pin runs.
        [Fact(Skip = "pending T367 (STORY-380 AC4)")]
        public void NoPathFromEitherThumbsSurfaceReachesEitherRepository() => Assert.Fail("pending T367");
    }
}
