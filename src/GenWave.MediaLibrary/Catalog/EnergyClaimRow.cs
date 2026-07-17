namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Projection returned by <see cref="MediaRepository.ListEnergyClaimsAsync"/> — carries the
/// minimum columns needed to run energy analysis without a second round-trip: the row's id,
/// file path, and existing cue points (already set by prior enrichment or cue analysis pass).
/// </summary>
sealed class EnergyClaimRow
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public double? CueInSec { get; set; }
    public double? CueOutSec { get; set; }
}
