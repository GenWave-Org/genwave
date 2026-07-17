namespace GenWave.MediaLibrary.Options;

/// <summary>
/// Configuration for the media library service (config section "Library"). The scan is periodic and
/// stat-only — the reliable baseline on network mounts where filesystem watchers are unreliable
/// (PRD §5.4).
/// </summary>
public sealed class LibraryOptions
{
    public const string Section = "Library";

    /// <summary>Root of the media tree the engine also sees (mounted at /media).</summary>
    public string MediaRoot { get; set; } = "/media";

    /// <summary>How often the incremental discovery scan runs.</summary>
    public int ScanIntervalSeconds { get; set; } = 60;

    /// <summary>How many files are enriched concurrently — kept modest to limit disk/mount impact.</summary>
    public int EnrichmentConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>Supported file extensions (lower-case, with the dot). v1: flac + mp3.</summary>
    public IReadOnlyList<string> SupportedExtensions { get; set; } = [".flac", ".mp3"];
}
