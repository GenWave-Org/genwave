namespace GenWave.MediaLibrary.YearLookup;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of a MusicBrainz <c>GET /recording?query=...&amp;fmt=json</c> response (SPEC F48.1) —
/// only the fields <see cref="MusicBrainzYearLookup"/> needs.
/// </summary>
sealed record MusicBrainzRecordingSearchResponse(
    [property: JsonPropertyName("recordings")] List<MusicBrainzRecording>? Recordings);
