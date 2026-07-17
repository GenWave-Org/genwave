namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Projection returned by <see cref="MediaRepository.ListYearLookupClaimsAsync"/> — carries the
/// minimum columns needed to attempt a MusicBrainz year lookup without a second round-trip: the
/// row's id and its tag values. Mirrors <see cref="BpmClaimRow"/>'s shape (SPEC F48.3).
/// </summary>
sealed class YearLookupClaimRow
{
    public long Id { get; set; }
    public string? Artist { get; set; }
    public string? Title { get; set; }
    public string? Album { get; set; }
}
