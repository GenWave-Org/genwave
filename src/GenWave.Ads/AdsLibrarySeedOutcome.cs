namespace GenWave.Ads;

/// <summary>Result of one <see cref="AdsLibrarySeeder.SeedAsync"/> attempt (SPEC F159.1, PLAN T396).</summary>
public enum AdsLibrarySeedOutcome
{
    /// <summary>A library named <see cref="AdsOptions.LibraryName"/> already existed — nothing was
    /// written.</summary>
    AlreadySeeded,

    /// <summary>The library did not exist and was created.</summary>
    Seeded,

    /// <summary>The create/lookup step failed. Nothing is left half-done — a bare create-if-absent has
    /// no partial state to clean up — so the next boot simply retries (F27.6's own "retry next boot"
    /// posture, mirrored here).</summary>
    Failed,
}
