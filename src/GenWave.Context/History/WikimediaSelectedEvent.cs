namespace GenWave.Context.History;

using System.Text.Json.Serialization;

/// <summary>
/// One entry of a <see cref="WikimediaOnThisDayResponse.Selected"/> list — <see cref="Text"/> and
/// <see cref="Year"/> only. Wikimedia's real reply carries a much larger <c>pages</c> array per entry
/// (thumbnails, full article extracts, wikibase items, ...) that this record deliberately never
/// deserializes: <see cref="HistoryContextProvider"/> has no use for any of it, and none of it belongs
/// in the day-file cache this type's own caller trims down to (see
/// <see cref="HistoryDayCacheEntry"/>'s own remarks) — smaller cache files, and no unused HTML-bearing
/// fields (an article <c>extract</c> can carry markup) sitting on disk unread.
/// </summary>
sealed record WikimediaSelectedEvent(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("year")] int? Year);
