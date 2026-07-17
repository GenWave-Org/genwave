namespace GenWave.MediaLibrary.YearLookup;

using System.Text.Json.Serialization;

/// <summary>
/// One candidate in a <see cref="MusicBrainzRecordingSearchResponse"/> (SPEC F48.1-F48.2 wire shape).
/// <see cref="FirstReleaseDate"/> is the fallback year source when no <see cref="Releases"/> entry
/// carries a parseable date.
/// </summary>
sealed record MusicBrainzRecording(
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("artist-credit")] List<MusicBrainzArtistCredit>? ArtistCredit,
    [property: JsonPropertyName("releases")] List<MusicBrainzRelease>? Releases,
    [property: JsonPropertyName("first-release-date")] string? FirstReleaseDate);
