namespace GenWave.MediaLibrary.YearLookup;

using System.Text.Json.Serialization;

/// <summary>The underlying artist entity of a <see cref="MusicBrainzArtistCredit"/> (SPEC F48.2 wire shape).</summary>
sealed record MusicBrainzArtist([property: JsonPropertyName("name")] string? Name);
