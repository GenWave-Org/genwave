namespace GenWave.MediaLibrary.YearLookup;

using System.Text.Json.Serialization;

/// <summary>
/// One entry of a <see cref="MusicBrainzRecording"/>'s <c>artist-credit</c> array (SPEC F48.2 wire
/// shape). <see cref="Name"/> is the credited (as-billed) name; <see cref="Artist"/> carries the
/// underlying artist entity's own canonical name — the two can differ, so the match check considers
/// both (F48.2's "any artist-credit name matches").
/// </summary>
sealed record MusicBrainzArtistCredit(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("artist")] MusicBrainzArtist? Artist);
