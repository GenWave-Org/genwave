// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L4ImmutabilityProbe;

/// <summary>An ordinary immutable record — init-only properties only, the exact shape every real
/// Abstractions record already takes. Proves the rule doesn't fail indiscriminately.</summary>
public sealed record CleanRecordFixture(string Name)
{
    public int Extra { get; init; } = 1;
}
