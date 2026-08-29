// STORY-380 — The knobs and the laws: L5 reserves Gardener, thumbs never cross into the other
// ledgers (SPEC F155.2/F155.3 · PLAN T357, T367)
//
// BDD specification — xUnit. PENDING until T357/T367. T357 adds `GenWave.Host.Gardener` to
// `HostReservedNamespaces.Entries` and a probe fixture under `Fixtures/L5Probe/Gardener`, proven
// against `HostNamespaceTripwire.FindViolations` — a DELIBERATE INVERSION of
// Story292_HostTripwire.cs's own `ReservedHit` idiom (probe-local reservation list + fixture under
// `GenWave.Architecture.Tests.Fixtures.*`, decoupled from Host's real type graph), not a repeat of
// it: this fixture sits in the LITERAL `GenWave.Host.Gardener` namespace and is checked against the
// REAL seeded `HostReservedNamespaces.Entries`, so the fact proves T357's own entry actually reds —
// see `ScenarioL5ReservesTheNamespace`'s own remarks for the full rationale. T357 itself builds the
// fixture and wires it in.
// T367 builds the disjointness pin: a call-graph scan over `ProductionAssemblies.Host` (mirrors
// Story366_AnnounceSchemeFenceTwoCarriers.cs's `AnnounceSchemeFence`, but walking call sites the
// way `MemberCallSiteScan` already does for other laws here, not attribute carriers) proving no
// path from `SpectatorThumbsController` or the booth-log station-thumb action ever reaches
// `MediaRatingRepository` or `PersonaTasteAccrualRepository` — the three petals' write paths stay
// disjoint by construction.
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureGardenerNamespaceAndDisjointness
{
    public sealed class ScenarioL5ReservesTheNamespace
    {
        // Given a type under GenWave.Host.Gardener, When the fitness suite runs. Exercises the REAL
        // seeded HostReservedNamespaces.Entries (T357 added the Gardener row) against a fixture-only
        // subject (never a production type). T357 review LOW-4: this is a DELIBERATE INVERSION of
        // Story292_HostTripwire.cs's own ReservedHit idiom, not a continuation of it —
        // Story292's own probe uses a PROBE-LOCAL reservation list pointed at a fixture namespace
        // under GenWave.Architecture.Tests.Fixtures.L5Probe.*, decoupling the proof from Host's real
        // type graph (its own remarks: "this proof stays decoupled from Host's real, live type
        // graph"). Here the goal is the opposite: prove T357's OWN seeded entry actually reds, not
        // merely that the detector mechanism works in the abstract — so this fact reuses the REAL
        // HostReservedNamespaces.Entries and puts the fixture in the literal reserved namespace
        // (GenWave.Host.Gardener; a namespace is independent of assembly) instead. Strictly stronger
        // than Story292's own shape for THIS one purpose, at the cost of the decoupling Story292
        // itself deliberately chose — never claim this as "the same idiom".
        [Fact]
        public void L5FailsNamingIt()
        {
            // The fixture's own C# namespace is literally GenWave.Host.Gardener (a namespace is
            // independent of assembly) — not GenWave.Architecture.Tests.Fixtures.L5Probe.Gardener,
            // the folder path it lives under — precisely so HostReservedNamespaces.Entries' real
            // "GenWave.Host.Gardener" reservation matches it for real.
            var violations = HostNamespaceTripwire.FindViolations(
                [typeof(GenWave.Host.Gardener.ViolatesReservation)], HostReservedNamespaces.Entries);

            var violation = Assert.Single(violations);
            Assert.Equal(LawId.L5, violation.LawId);
            Assert.Equal("GenWave.Host.Gardener.ViolatesReservation", violation.Member);
            Assert.Contains("F155.2", violation.Detail);
        }
    }

    public sealed class ScenarioThreeWayDisjointness
    {
        // Given the production assemblies, When the disjointness pin runs.
        [Fact(Skip = "pending T367 (STORY-380 AC4)")]
        public void NoPathFromEitherThumbsSurfaceReachesEitherRepository() => Assert.Fail("pending T367");
    }
}
