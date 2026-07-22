namespace GenWave.Core.Abstractions;

/// <summary>
/// Write seam for <c>station.booth_log</c> (SPEC F72.1, F72.3, STORY-195). Retention (default 14
/// days, <c>BoothLog:RetentionDays</c>) is enforced in the SAME statement/transaction as the insert —
/// see the concrete store's own remarks for why insert-time eviction, not a separate job, is the
/// honest mechanism here. Implementations MUST be safe to call from a background drain loop off the
/// hot path — see <c>GenWave.MediaLibrary.Station.BoothLogWriter</c>, the
/// <see cref="IStationEventSink"/> consumer that feeds this seam.
/// </summary>
public interface IBoothLogAppender
{
    /// <summary>
    /// Appends one narrative row (<paramref name="kind"/>, <paramref name="summary"/>), stamped
    /// <c>now()</c>. <paramref name="personaId"/> (SPEC F84.6, STORY-215) is the persona active on
    /// air at write time for a TRACK-START row — <see langword="null"/> for every other kind, or a
    /// persona-less airing. <paramref name="artist"/> (SPEC F84.1, STORY-215, PLAN T70) is that same
    /// track's artist, captured the same way and for the same reason: the accrual write path needs a
    /// STRUCTURED artist to build an artist-predicate rule from, never a regex over
    /// <paramref name="summary"/>'s narrative prose. Never surfaced through <see cref="IBoothLogReader"/>
    /// — read directly by the accrual store only. <see langword="null"/> for every non-track row or a
    /// track aired with no known artist. <paramref name="pick"/> (SPEC F86.1, STORY-217, PLAN T73) is
    /// that same track's persona-pick stamp — the caller's already-serialized jsonb text (see
    /// <c>GenWave.Core.Domain.BoothLogPickStampSerializer</c>), or <see langword="null"/> for every
    /// non-track row, an engine-initiated play, or a persona-off pick. Never backfilled.
    /// </summary>
    Task AppendAsync(string kind, string summary, long? personaId, string? artist, string? pick, CancellationToken ct);
}
