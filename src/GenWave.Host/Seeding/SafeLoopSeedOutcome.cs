namespace GenWave.Host.Seeding;

/// <summary>Result of one <see cref="SafeLoopSeeder.SeedAsync"/> attempt (SPEC F27.6, STORY-080).</summary>
public enum SafeLoopSeedOutcome
{
    /// <summary>The marker was already present — nothing was read or written (F27.6 AC2).</summary>
    AlreadySeeded,

    /// <summary>
    /// The full pipeline succeeded: the <c>safe</c> library exists, the seed segment is authored, the
    /// SafeScope overlay was written (unless an operator value already existed), and the marker is
    /// now set.
    /// </summary>
    Seeded,

    /// <summary>
    /// A step failed. Nothing is marked complete, so the next boot retries (F27.6 AC4). Any partial
    /// progress from this attempt — a created library, or a library that already holds the rendered
    /// row — is left in place: the retry finds it via the library's media count and reuses it rather
    /// than re-rendering, so a failure between the render and the marker write can never leave two
    /// "Please Stand By" rows behind.
    /// </summary>
    Failed,
}
