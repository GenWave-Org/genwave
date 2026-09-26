namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's flat projection of <see cref="AdSpotRepository.ListPendingRetiresAsync"/>'s own two-column
/// read (gh-#854) — the <see cref="AdSpotRow"/> precedent, narrowed to just the pair this one query
/// needs rather than the full <c>ad_spot</c> column list.
/// </summary>
sealed class PendingAdSpotRetireRow
{
    public long Id { get; set; }
    public long PendingRetireMediaId { get; set; }
}
