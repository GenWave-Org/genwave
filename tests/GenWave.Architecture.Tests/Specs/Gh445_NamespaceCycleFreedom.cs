// gh-#445 — L10: namespace-level cycle freedom (Dean's contributor-proofing ask, 2026-08-09;
// promoted to a named law at the 2026-08-14 /design session, PLAN T351, SPEC F145.6 rider).
//
// Project-LEVEL cycles are structurally impossible (MSBuild refuses a circular
// ProjectReference), so the dependency laws never needed to state them. But cycles BETWEEN
// NAMESPACES INSIDE one assembly are exactly the tangle contributors introduce — two Host
// subsystems reaching into each other until neither can be extracted (the F105.4 graduation
// rule assumes a subsystem CAN leave Host; a namespace cycle is how it becomes unable to).
//
// Granularity: slices = the first namespace segment below each project root ("GenWave.Host.(*)"
// → Api, Engine, Playout, Configuration, …). Flat-namespaced projects (most types directly in
// the root, e.g. GenWave.Tts) contribute few or no slices and pass vacuously — the law guards
// wherever internal structure exists, which today is chiefly Host, MediaLibrary, and Context.
//
// Detector: NamespaceCycleFence (Support/NamespaceCycleFence.cs) reports through the SAME
// LawId/ExemptionBaseline mechanism L1–L9 use (DependencyLawAssert.AssertNone), not a law-local
// baseline dict — the two original 2026-08-09 tangles (GenWave.Core Events->Abstractions,
// GenWave.Host Api->Stats) were untangled in the same gh-#445 pass, so there was never a live
// exemption row to carry forward into the shared mechanism.
using ArchUnitNET.Loader;
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public sealed class FeatureNamespaceCycleFreedom
{
    public static readonly TheoryData<string> ProjectRoots =
    [
        "GenWave.Abstractions",
        "GenWave.Core",
        "GenWave.Context",
        "GenWave.Host",
        "GenWave.Loudness",
        "GenWave.MediaLibrary",
        "GenWave.Orchestration",
        "GenWave.Tts",
    ];

    public sealed class ScenarioNoNamespaceCyclesWithinAnyProject
    {
        [Theory]
        [MemberData(nameof(ProjectRoots), MemberType = typeof(FeatureNamespaceCycleFreedom))]
        public void Namespaces_below_the_project_root_are_free_of_cycles(string projectRoot)
        {
            var violations = NamespaceCycleFence.FindViolations(projectRoot, ProductionArchitecture.Instance);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }
    }

    public sealed class ScenarioASyntheticNamespaceCycleIsRed
    {
        // A genuine two-slice cycle (Fixtures/L10Probe/SliceA.cs <-> SliceB.cs — a real compiled
        // property-type edge each way), loaded into its own fixture architecture — never a
        // production edit, the same "fixture architecture, real ArchUnitNET evaluation" precedent
        // Story290's L2 probe sets (its own fixtureArchitecture). Proves the mechanism genuinely
        // reaches a real cycle and attributes it to LawId.L10, not merely that today's real host has
        // none to find — that fact alone would stay green even if the L10 attribution silently
        // rotted to some other law id.
        static readonly ArchUnitArchitecture FixtureArchitecture = new ArchLoader()
            .LoadAssemblies(typeof(Fixtures.L10Probe.SliceA.TypeA).Assembly)
            .Build();

        [Fact]
        public void TheFenceReportsAGenuineCycleTaggedL10()
        {
            var violations = NamespaceCycleFence.FindViolations(
                "GenWave.Architecture.Tests.Fixtures.L10Probe", FixtureArchitecture);

            var violation = Assert.Single(violations);
            Assert.Equal(LawId.L10, violation.LawId);
            Assert.Contains("SliceA", violation.Detail, StringComparison.Ordinal);
            Assert.Contains("SliceB", violation.Detail, StringComparison.Ordinal);
        }
    }
}
