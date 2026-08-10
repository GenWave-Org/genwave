using GenWave.Core.Domain;

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
    /// Appends one narrative row, stamped <c>now()</c>. See <see cref="BoothLogAppendRequest"/>'s own
    /// field docs for what each field carries, and when it is <see langword="null"/> — this method
    /// derives nothing from <paramref name="request"/> beyond what is already stamped on it.
    /// </summary>
    Task AppendAsync(BoothLogAppendRequest request, CancellationToken ct);
}
