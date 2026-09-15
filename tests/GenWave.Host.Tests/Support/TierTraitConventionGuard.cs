namespace GenWave.Host.Tests.Support;

/// <summary>
/// The Host tier guard (SPEC F177.3, STORY-439, PLAN T504) — the reflection twin of
/// <c>GenWave.MediaLibrary.Tests.IntegrationTraitConventionGuard</c>: any test class that takes a
/// <c>KokoroFixture</c> (constructor, <c>IClassFixture&lt;&gt;</c> or <c>ICollectionFixture&lt;&gt;</c>)
/// or any type in <see cref="StackFixtureTypes"/>, and lacks <c>[Trait("Category", "Integration")]</c>
/// at class level or on every fact, is a violation — it would try to start a container in the PR lane.
/// </summary>
/// <remarks>Skeleton at plan time — throws until T504 lands.</remarks>
internal static class TierTraitConventionGuard
{
    /// <summary>Fixture types that need a full stack or an audio capture — tier 2 by definition.
    /// Empty until a stack fixture exists; the guard's own list, extended in place.</summary>
    public static IReadOnlyList<Type> StackFixtureTypes { get; } = [];

    /// <summary>Full names of the offending classes (one entry per class, never per fact).</summary>
    public static IReadOnlyList<string> FindViolations(
        IEnumerable<Type> types, IReadOnlyCollection<Type>? stackFixtureTypes = null) =>
        throw new NotImplementedException("pending: T504 — TierTraitConventionGuard (STORY-439)");
}
