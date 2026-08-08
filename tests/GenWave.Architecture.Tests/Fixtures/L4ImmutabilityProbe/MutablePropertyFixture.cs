// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path — proves AbstractionsImmutability actually flags
// a genuinely public, non-init setter instead of always passing.

namespace GenWave.Architecture.Tests.Fixtures.L4ImmutabilityProbe;

/// <summary>Stands in for a real Abstractions type carrying the one property shape L4-immutability
/// forbids: a settable public property that is neither <c>init</c> nor privately gated.</summary>
public sealed class MutablePropertyFixture
{
    public string Name { get; set; } = string.Empty;
}
