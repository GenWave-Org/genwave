namespace GenWave.Host.Playout;

/// <summary>
/// A single entry in the per-station play history ring. Immutable snapshot of what aired.
/// <see cref="EndedAt"/> is null while the track is still on-air; it is stamped with the
/// <see cref="StartedAt"/> of the next entry when the ring advances.
/// </summary>
/// <param name="DurationMs">
/// Track duration, if known (SPEC F50.3); null for engine-initiated plays and <c>tts:*</c> patter
/// (F50.6) — never fabricated.
/// </param>
public sealed record PlayHistoryEntry(
    string StationId,
    string MediaId,
    string? Title,
    string? Artist,
    double GainDb,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int? DurationMs);
