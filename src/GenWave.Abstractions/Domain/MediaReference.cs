namespace GenWave.Core.Domain;

/// <summary>
/// The full catalog projection of a track (PRD §4.2, SEAM 2). Richer than <see cref="MediaItem"/>
/// because future consumers (criteria queries, UIs) need the catalog; <see cref="MediaItem"/> is a
/// narrowing of this. The free metadata is nullable until the file has been enriched (PRD §8).
/// </summary>
public sealed record MediaReference(
    string   MediaId,
    string   Locator,
    string   Title,
    Loudness Loudness,
    // free metadata captured at enrichment (PRD §8); nullable until enriched
    int?     DurationMs,
    int?     SampleRate,
    short?   Channels,
    int?     BitrateKbps,
    string?  Artist,
    string?  Album,
    string?  Genre,
    int?     Year,
    CuePoints? Cue = null,
    double?    IntroEnergy = null,
    double?    OutroEnergy = null);
