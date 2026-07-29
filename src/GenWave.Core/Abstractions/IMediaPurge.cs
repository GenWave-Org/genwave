using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The explicit operator purge for long-unavailable media rows (gh-#113). Its own seam — not a
/// method on <see cref="IAdminMediaWrite"/> — mirroring <see cref="IMediaExplicitOverride"/>'s
/// precedent: a hard-delete is the one admin write with a genuinely different blast radius, and
/// keeping it here costs the wider write contract (and every test double implementing it) nothing.
/// </summary>
public interface IMediaPurge
{
    /// <summary>
    /// Hard-deletes every row that has been <c>unavailable</c> longer than
    /// <paramref name="olderThanDays"/> days (per its <c>unavailable_since</c> stamp; a NULL stamp
    /// is never purgeable), cascading to dependent rows (<c>library.media_rating</c>, via its
    /// <c>ON DELETE CASCADE</c> FK). Counting, the tripwire, and the delete happen in ONE SQL
    /// statement, so the decision can never race a concurrent scan flip.
    ///
    /// Tripwire (the mount-outage guard): when the candidates exceed half the whole library
    /// (<see cref="MediaPurgeOutcome.TripwireTripped"/>), NOTHING is deleted — a shrunk mount that
    /// flips most of the catalog unavailable must never be compounded by an operator purging it
    /// away before the mount comes back. Exactly half is allowed; "more than half" refuses.
    ///
    /// <paramref name="dryRun"/> true counts (and trips the tripwire) without deleting — the
    /// confirm-dialog figure. The outcome's counts mean the same thing in both modes.
    /// </summary>
    /// <param name="olderThanDays">Minimum whole days a row must have been unavailable; at least 1 (fail-fast below).</param>
    /// <param name="dryRun">True counts candidates without deleting anything.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<MediaPurgeOutcome> PurgeUnavailableAsync(int olderThanDays, bool dryRun, CancellationToken ct);
}
