// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L4ImmutabilityProbe;

/// <summary>An enum — excluded outright by <see cref="Support.AbstractionsImmutability"/> before
/// any member is even inspected. Proves the detector skips enums rather than false-positiving on
/// their compiler-generated, reflection-visible-as-public <c>value__</c> field.</summary>
public enum CleanEnumFixture
{
    First,
    Second,
}
