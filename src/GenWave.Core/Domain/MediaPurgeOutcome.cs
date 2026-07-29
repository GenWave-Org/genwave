namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IMediaPurge.PurgeUnavailableAsync"/> (gh-#113): the counts a
/// single SQL statement read atomically alongside the (possibly withheld) delete.
/// </summary>
/// <param name="Candidates">Rows unavailable longer than the requested window — what a purge would (or did) delete.</param>
/// <param name="LibraryTotal">Every row in <c>library.media</c> at decision time — the tripwire's denominator.</param>
/// <param name="Deleted">Rows actually deleted: 0 on a dry run or a tripped tripwire, else <paramref name="Candidates"/>.</param>
public sealed record MediaPurgeOutcome(int Candidates, int LibraryTotal, int Deleted)
{
    /// <summary>
    /// True when the candidates exceed half the library (gh-#113's mount-outage guard) — the purge
    /// refused to delete anything. Exactly half is allowed; strictly more than half trips.
    /// </summary>
    public bool TripwireTripped => Candidates * 2 > LibraryTotal;
}
