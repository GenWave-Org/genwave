// gh-#445 — L7: namespace-level cycle freedom (Dean's contributor-proofing ask, 2026-08-09).
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
using ArchUnitNET.Fluent.Slices;
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

    /// <summary>
    /// Pre-existing tangles recorded 2026-08-09 (gh-#445, where each carries a fix sketch) — the
    /// guard's job is stopping NEW cycles while these two are untangled. Shrink-to-fit: fixing a
    /// cycle makes its row here STALE and the spec fails until the row is deleted, so this
    /// baseline can only ever shrink. Mirrors the F105.2 exemption philosophy (named, dated,
    /// reasoned) without claiming a LawId — law promotion is gh-#445's recorded /design step.
    /// </summary>
    static readonly IReadOnlyDictionary<string, string[]> BaselinedCycles = new Dictionary<string, string[]>
    {
        ["GenWave.Core"] = ["Events -> Abstractions"], // NoOpStationEventSink lives in Events; IStationEventSink's signature reaches back
        ["GenWave.Host"] = ["Api -> Stats"],           // DockerContainerStatsSource returns Api-namespace DTOs (gh-#148 shape)
    };

    public sealed class ScenarioNoNamespaceCyclesWithinAnyProject
    {
        [Theory]
        [MemberData(nameof(ProjectRoots), MemberType = typeof(FeatureNamespaceCycleFreedom))]
        public void Namespaces_below_the_project_root_are_free_of_cycles(string projectRoot)
        {
            // Evaluate() + hand-rolled assert, not the ArchUnitNET.xUnit Check() extension — the
            // suite deliberately carries only the core analysis package (Story290's pattern), and
            // the failure text here lists every cyclic slice pair rather than a generic rule name.
            var failures = SliceRuleDefinition.Slices()
                .Matching($"{projectRoot}.(*)")
                .Should()
                .BeFreeOfCycles()
                .Evaluate(ProductionArchitecture.Instance)
                .Where(result => !result.Passed)
                .Select(result => result.Description)
                .ToList();

            var baseline = BaselinedCycles.GetValueOrDefault(projectRoot, []);

            var unexpected = failures.Where(f => !baseline.Any(f.Contains)).ToList();
            Assert.True(
                unexpected.Count == 0,
                $"NEW namespace cycle(s) in {projectRoot} — not in the gh-#445 baseline, untangle before merging:\n" +
                string.Join("\n", unexpected));

            foreach (var marker in baseline)
            {
                Assert.True(
                    failures.Any(f => f.Contains(marker)),
                    $"stale baseline row '{marker}' for {projectRoot} — that cycle is fixed 🎉; " +
                    "delete the row from BaselinedCycles so the guard tightens (gh-#445 shrink-to-fit)");
            }
        }
    }
}
