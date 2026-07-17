namespace GenWave.MediaLibrary.YearLookup;

using System.Text.Json.Serialization;

/// <summary>
/// One entry of a <see cref="MusicBrainzRecording"/>'s <c>releases</c> array (SPEC F48.2 wire shape).
/// <see cref="Date"/> may be absent, or shaped "YYYY", "YYYY-MM", or "YYYY-MM-DD" — MusicBrainz's own
/// partial-date convention.
/// </summary>
sealed record MusicBrainzRelease([property: JsonPropertyName("date")] string? Date);
