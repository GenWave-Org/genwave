// Fixture type for STORY-290 AC5's self-exercising negative probe (Story290_DependencyLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L2Probe.Elsewhere;

/// <summary>Outside the confined namespace but touches nothing forbidden — proves the rule doesn't
/// fail indiscriminately.</summary>
public sealed class StaysClean
{
    public int Value => 1;
}
