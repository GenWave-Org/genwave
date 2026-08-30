namespace GenWave.Abstractions.Playout;

/// <summary>
/// A <see cref="SegmentEnvelope.Rotation"/>'s music-pool narrowing rule (SPEC F152.1, STORY-372): a
/// "deep cuts" style constraint layered on top of every other envelope narrowing this record's
/// sibling fields already apply. At least one of <see cref="MaxPlays"/>/<see cref="NotAiredWithinDays"/>
/// must be set for the predicate to mean anything — that "at least one" rule is validated at the
/// edges that accept operator input (the Shows API <c>PUT</c>, the catalog import), never here: this
/// record stays a plain value carrier, both fields legally null at construction. This is a
/// deliberate asymmetry with the sibling <see cref="EnergyRange"/>, which DOES validate at
/// construction: a corrupted <c>station.show.envelope</c> row must never throw mid-airing (F152.4's
/// never-silence posture), so validation stays at the edges here instead, and T360's read path
/// normalizes a both-null predicate to <c>Rotation = null</c> so the relax ladder never stamps
/// <c>RotationRelax = 0</c> for a predicate that filters nothing.
///
/// <para>
/// <see cref="MaxPlays"/> (SPEC F152.2) admits a row when
/// <c>coalesce(play_count, 0) &lt;= MaxPlays</c> — null means "no play-count ceiling".
/// </para>
///
/// <para>
/// <see cref="NotAiredWithinDays"/> (SPEC F152.2) admits a row when
/// <c>last_aired_at is null or last_aired_at &lt; now() − days</c> — null means "no recency floor".
/// </para>
/// </summary>
public sealed record RotationPredicate(int? MaxPlays = null, int? NotAiredWithinDays = null);
