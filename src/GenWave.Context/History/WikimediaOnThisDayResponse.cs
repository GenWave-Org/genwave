namespace GenWave.Context.History;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of a Wikimedia On-This-Day <c>GET /feed/v1/wikipedia/en/onthisday/selected/{MM}/{dd}</c>
/// response (SPEC F109.1) — only the one top-level section <see cref="HistoryContextProvider"/> reads.
/// <c>selected</c> vs <c>events</c> decided at T228 build time against a real payload (curl'd from
/// api.wikimedia.org — keyless): see <see cref="HistoryContextProvider"/>'s own remarks for the exact
/// shape and why <c>selected</c> won.
/// </summary>
sealed record WikimediaOnThisDayResponse(
    [property: JsonPropertyName("selected")] IReadOnlyList<WikimediaSelectedEvent>? Selected);
