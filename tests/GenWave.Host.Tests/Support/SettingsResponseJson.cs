using System.Text.Json;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Parses a GET <c>/api/settings</c> response body as raw JSON and locates one row by its wire
/// <c>key</c> field — the "parse GET body → find key row" arrange step Story482's own scenario ctors
/// (<c>Story482_CrosstalkShowCheckboxes.cs</c>) shared verbatim (PLAN T585 review finding 3). Reads
/// the RAW <see cref="JsonElement"/>, never round-tripped back through <c>SettingDto</c>, so a renamed
/// wire field cannot pass silently (mirrors T580 review finding R1).
/// </summary>
internal static class SettingsResponseJson
{
    /// <summary>GETs <paramref name="route"/> and parses the body as a <see cref="JsonElement"/> array.</summary>
    public static JsonElement GetJson(HttpClient client, string route) =>
        JsonDocument.Parse(client.GetStringAsync(route).GetAwaiter().GetResult()).RootElement;

    /// <summary>Finds the one row in a GET <c>/api/settings</c> body (as parsed by <see cref="GetJson"/>)
    /// whose <c>key</c> field equals <paramref name="key"/>.</summary>
    public static JsonElement FindKey(this JsonElement settingsBody, string key) =>
        settingsBody.EnumerateArray().Single(row => row.GetProperty("key").GetString() == key);
}
