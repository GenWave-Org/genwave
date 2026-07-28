namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Projection returned by <see cref="MediaRepository.ListExplicitClassificationClaimsAsync"/> —
/// carries the minimum columns needed to attempt an explicit-classification completion without a
/// second round-trip: the row's id and its title/artist tag values. Mirrors
/// <see cref="MoodTagClaimRow"/>'s shape, minus genre — the classification prompt is deliberately
/// title/artist only (SPEC F95.3, gh-#174's lesson).
/// </summary>
sealed class ExplicitClassificationClaimRow
{
    public long Id { get; set; }
    public string? Artist { get; set; }
    public string? Title { get; set; }
}
