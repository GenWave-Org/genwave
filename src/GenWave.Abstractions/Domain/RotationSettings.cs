namespace GenWave.Core.Domain;

/// <summary>
/// Live-tunable rotation knobs (SPEC F41.6) read through <see cref="Abstractions.IRotationSettingsProvider"/>.
/// Mirrors <see cref="CadenceConfig"/>'s shape: a small value carrying only the two knobs Story-134
/// (anti-repeat window) and Story-135 (artist separation) need — not a strategy engine.
/// </summary>
public sealed record RotationSettings
{
    /// <summary>
    /// How many recently-aired media ids <c>PlayoutFeeder</c> remembers for repeat-avoidance
    /// (<see cref="Abstractions.IMediaCatalog.GetRotationCandidateAsync"/>'s <c>orderedRecentIds</c>
    /// list). <c>0</c> disables anti-repeat — every advance is remembered for zero ticks, so the
    /// list handed to selection is always empty.
    /// </summary>
    public int RecentWindow { get; init; } = 20;

    /// <summary>
    /// No same artist within the last N music selections (a preference tier that relaxes rather
    /// than excludes, F41.3). <c>0</c> disables the artist-separation tier.
    /// </summary>
    public int ArtistSeparation { get; init; } = 2;
}
