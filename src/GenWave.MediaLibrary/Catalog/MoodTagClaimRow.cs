namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Projection returned by <see cref="MediaRepository.ListMoodTagClaimsAsync"/> — carries the minimum
/// columns needed to attempt a mood-tag completion without a second round-trip: the row's id and its
/// tag values. Mirrors <see cref="YearLookupClaimRow"/>'s shape (SPEC F85.2).
/// </summary>
sealed class MoodTagClaimRow
{
    public long Id { get; set; }
    public string? Artist { get; set; }
    public string? Title { get; set; }
    public string? Genre { get; set; }
}
