using ArchUnitNET.Fluent.Slices;
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L10's detector (gh-#445, promoted to a named law at PLAN T351, SPEC F145.6 rider — see
/// <c>Gh445_NamespaceCycleFreedom.cs</c>'s own remarks for the granularity rationale: slices = the
/// first namespace segment below each project root). Evaluate() + a hand-translated
/// <see cref="LawViolation"/>, not the ArchUnitNET.xUnit <c>Check()</c> extension — the suite
/// deliberately carries only the core analysis package (Story290's pattern) — reporting through the
/// SAME <see cref="LawId"/>/<see cref="DependencyLawAssert"/> mechanism every other law uses, rather
/// than a law-local baseline dictionary, so a cycle here is exempted (or not) the identical
/// named-dated-reasoned way an L1–L9 violation would be.
///
/// <b>Granularity: one violation per offending slice pair, all attributed to the project root.</b>
/// Unlike L1–L9's per-TYPE offenders, this law's unit of offense is the project's own internal
/// namespace structure — there is no single type to name as "the" cycle culprit, so
/// <paramref name="projectRoot"/> itself is <see cref="LawViolation.Member"/>, and ArchUnitNET's own
/// cyclic-slice description (which names the offending slices) is <see cref="LawViolation.Detail"/>.
/// </summary>
internal static class NamespaceCycleFence
{
    public static IReadOnlyList<LawViolation> FindViolations(string projectRoot, ArchUnitArchitecture architecture) =>
        SliceRuleDefinition.Slices()
            .Matching($"{projectRoot}.(*)")
            .Should()
            .BeFreeOfCycles()
            .Evaluate(architecture)
            .Where(result => !result.Passed)
            .Select(result => new LawViolation(LawId.L10, projectRoot, result.Description))
            .ToList();
}
