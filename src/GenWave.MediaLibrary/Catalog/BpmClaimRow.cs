namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Projection returned by <see cref="MediaRepository.ListBpmClaimsAsync"/> — carries the minimum
/// columns needed to run BPM analysis without a second round-trip: the row's id, file path, and
/// existing cue points (already set by prior enrichment or cue analysis pass). Mirrors
/// <see cref="EnergyClaimRow"/> exactly (SPEC F46.3).
/// </summary>
sealed class BpmClaimRow
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public double? CueInSec { get; set; }
    public double? CueOutSec { get; set; }
}
