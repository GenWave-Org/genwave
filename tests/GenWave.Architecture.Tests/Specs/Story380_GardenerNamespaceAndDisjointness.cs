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
// disjoint by construction. T367 review MED-3: ScenarioTheScannerCatchesAnAsyncLambdaAndLocalFunction
// seeds the SAME scanner at a self-contained probe fixture (Fixtures/F155Probe/) reaching a stand-in
// "forbidden" repository through an async lambda AND an async local function — the Story323
// "L7L8Probe"/L9Probe precedent of proving the DETECTOR itself, decoupled from the real production
// type graph, alongside the real production fact (ScenarioThreeWayDisjointness) that stays seeded at
// the two real actions.
using GenWave.Architecture.Tests.Support;
using GenWave.Host.Tests.Support;

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
        // Given the production assemblies, When the disjointness pin runs (GardenerThumbDisjointnessScan,
        // T367): a bounded IL call-graph BFS seeded at SpectatorThumbsController.PostThumb and
        // BoothLogController.ThumbStation (never their whole controller classes — BoothLogController
        // also hosts ThumbTaste, which legitimately calls IPersonaTasteAccrualStore), crossing every
        // GenWave interface it reaches into its REAL, composition-root-resolved effective adapter
        // (SeamCompositionSnapshot.Capture — SEAMS.md's own generator, never a hand-typed re-statement).
        //
        // MUTANT PROOF (run by hand — PLAN T367's own VERIFY step, not part of this suite): temporarily
        // making BoothLogController.ThumbStation call accrual.ThumbAsync(...) (the very
        // IPersonaTasteAccrualStore already sitting in that class's own constructor, for ThumbTaste)
        // reds this fact, naming PersonaTasteAccrualRepository reachable via ThumbStation — reverted
        // immediately after proving it.
        [Fact]
        public void NoPathFromEitherThumbsSurfaceReachesEitherRepository()
        {
            var interfaceAdapters = SeamCompositionSnapshot.Capture(IsGenWaveInterface)
                .ToDictionary(
                    port => port.PortType.FullName
                        ?? throw new InvalidOperationException($"{port.PortType} has no FullName"),
                    port => port.Adapters.Single(a => a.IsEffective).AdapterType.FullName
                        ?? throw new InvalidOperationException($"{port.PortType} effective adapter has no FullName"),
                    StringComparer.Ordinal);

            var violations = GardenerThumbDisjointnessScan.FindViolations(
                ProductionAssemblies.AllProductionAssemblies(),
                interfaceAdapters,
                GardenerThumbDisjointnessScan.Roots,
                GardenerThumbDisjointnessScan.ForbiddenTypeFullNames);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }

        // The SAME "GenWave.* interface port" predicate SeamIndexDocument.IsGenWavePort uses (that
        // method is private to tools/SeamIndexGenerator, so this is a genuine second copy, not a
        // reachable shared one) — every non-GenWave interface (IOptions<T>, IOptionsMonitor<T>, ...) a
        // call site might reach stays outside this map by construction, and is correctly a dead end in
        // GardenerThumbDisjointnessScan's own type index regardless (class remarks).
        static bool IsGenWaveInterface(Type type) =>
            type.IsInterface && type.Namespace is { } ns
            && (ns == "GenWave" || ns.StartsWith("GenWave.", StringComparison.Ordinal));
    }

    public sealed class ScenarioTheScannerCatchesAnAsyncLambdaAndLocalFunction
    {
        // T367 review HIGH-1/MED-1's own mutant, permanently pinned as a CI fixture (MED-3) rather
        // than only a by-hand revert: Fixtures/F155Probe/ProbeAction.cs's
        // ReachesForbiddenViaLambdaAndLocalFunction reaches Fixtures/F155Probe/ForbiddenRepository.cs
        // once through an async LAMBDA and once through an async LOCAL FUNCTION — neither named
        // <MethodName>d__N the way a plain async method's own state machine is, the exact shape that
        // passed the OLD name-prefix redirect silently. Seeded against THIS test assembly (the probe
        // types live here, not in production), with an EMPTY interfaceAdapters map — the probe calls
        // nothing through an interface, so there is nothing to resolve.
        [Fact]
        public void AsyncLambdaReachabilityIsCaught()
        {
            var violations = GardenerThumbDisjointnessScan.FindViolations(
                [typeof(Fixtures.F155Probe.ProbeAction).Assembly],
                new Dictionary<string, string>(StringComparer.Ordinal),
                [("GenWave.Architecture.Tests.Fixtures.F155Probe.ProbeAction", "ReachesForbiddenViaLambdaAndLocalFunction")],
                ["GenWave.Architecture.Tests.Fixtures.F155Probe.ForbiddenRepository"]);

            // Two distinct MethodDefs on ForbiddenRepository are reachable here — its constructor
            // (new ForbiddenRepository()) AND Touch() — so two violations, not one; both must name it.
            Assert.Equal(2, violations.Count);
            Assert.All(violations, v =>
            {
                Assert.Equal(GardenerThumbDisjointnessScan.DisjointnessLawId, v.LawId);
                Assert.Equal("GenWave.Architecture.Tests.Fixtures.F155Probe.ForbiddenRepository", v.Member);
            });
            Assert.Contains(violations, v => v.Detail.Contains("Touch", StringComparison.Ordinal));
        }

        // T367 review MED-1: ProbeEntryForOverload calls the SECOND of two async overloads sharing a
        // name (Overload()/Overload(int), only the latter reaching ForbiddenRepository) — the exact
        // shape the OLD prefix search could resolve to the WRONG overload's state machine.
        [Fact]
        public void OverloadDisambiguationIsCorrect()
        {
            var violations = GardenerThumbDisjointnessScan.FindViolations(
                [typeof(Fixtures.F155Probe.ProbeAction).Assembly],
                new Dictionary<string, string>(StringComparer.Ordinal),
                [("GenWave.Architecture.Tests.Fixtures.F155Probe.ProbeAction", "ProbeEntryForOverload")],
                ["GenWave.Architecture.Tests.Fixtures.F155Probe.ForbiddenRepository"]);

            Assert.Equal(2, violations.Count);
            Assert.All(violations, v =>
                Assert.Equal("GenWave.Architecture.Tests.Fixtures.F155Probe.ForbiddenRepository", v.Member));
        }
    }
}
