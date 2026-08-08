// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L3Probe.Elsewhere;

/// <summary>Outside the seam list but touches nothing forbidden — proves the rule doesn't fail
/// indiscriminately.</summary>
public sealed class StaysClean
{
    public int Value => 1;
}
