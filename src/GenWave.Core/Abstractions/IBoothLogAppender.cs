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
    /// persona-less airing.
    /// </summary>
    Task AppendAsync(string kind, string summary, long? personaId, CancellationToken ct);
}
