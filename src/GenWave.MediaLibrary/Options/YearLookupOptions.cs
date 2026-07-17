using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Options;

/// <summary>
/// Configuration for the MusicBrainz release-year lookup (config section "Library:YearLookup",
/// SPEC F48). <see cref="Enabled"/> and <see cref="Endpoint"/> are live-editable (F48.5, the F19/F36
/// precedent) — read fresh per call via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>,
/// never boot-frozen.
/// </summary>
public sealed class YearLookupOptions
{
    public const string Section = "Library:YearLookup";

    /// <summary>Master kill switch (F48.5). false stops the backfill claim loop before its next tick.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>MusicBrainz web service base — recording search lives at "{Endpoint}/recording".</summary>
    public string Endpoint { get; set; } = "https://musicbrainz.org/ws/2";

    /// <summary>Match-score floor (0-100); a candidate below this is never accepted (F48.2).</summary>
    [Range(0, 100)]
    public int MinScore { get; set; } = 90;

    /// <summary>Per-request timeout budget in seconds — bounded per call, not on the shared HttpClient (F48.1).</summary>
    [Range(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 10;
}
