// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L4ImmutabilityProbe;

/// <summary>A public, non-const, non-readonly field — the field-shaped half of the same forbid,
/// proven independently of <see cref="MutablePropertyFixture"/>'s property-shaped one.</summary>
public sealed class MutableFieldFixture
{
    public int Count;
}
