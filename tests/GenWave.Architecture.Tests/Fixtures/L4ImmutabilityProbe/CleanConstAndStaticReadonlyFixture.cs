// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L4ImmutabilityProbe;

/// <summary>A static holder shaped like the real
/// <c>GenWave.Abstractions.Domain.MoodVocabulary</c>'s <c>const</c>/<c>static readonly</c>
/// members — proves the detector doesn't false-positive on either of the two allowed public-field
/// shapes.</summary>
public static class CleanConstAndStaticReadonlyFixture
{
    public const int Version = 1;

    public static readonly IReadOnlyList<string> Terms = ["a", "b"];
}
