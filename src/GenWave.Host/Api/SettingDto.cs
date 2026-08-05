using GenWave.Host.Configuration;

namespace GenWave.Host.Api;

/// <summary>
/// Shape of a single entry returned by <c>GET /api/settings</c>.
/// </summary>
/// <param name="Key">Configuration key (colon-separated, e.g. <c>Loudness:TargetLufs</c>).</param>
/// <param name="Value">Current effective value from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.</param>
/// <param name="Source">
///   <c>"override"</c> when the value came from the station.settings DB overlay;
///   <c>"default"</c> when no override row exists and the value is from env/appsettings.
/// </param>
/// <param name="ApplyMode">
///   <c>"live"</c> if the value takes effect immediately via IOptionsMonitor re-binding;
///   <c>"engine-restart"</c> if the value is stored but requires a Liquidsoap engine restart;
///   <c>"enrichment"</c> if the value only takes effect the next time a file is (re-)analyzed
///   (SPEC F44.3).
/// </param>
/// <param name="Kind">
///   <c>"boolean"</c> for toggle settings rendered as a checkbox;
///   <c>"number"</c> for numeric settings rendered as a number input;
///   <c>"choice"</c> for settings restricted to a closed set — see <paramref name="Choices"/>.
/// </param>
/// <param name="Unit">
///   Short unit label for display (e.g. <c>"LUFS"</c>, <c>"seconds"</c>).
///   Empty string for booleans.
/// </param>
/// <param name="Choices">
///   The closed set of valid <see cref="SettingChoice"/> value/label pairs, present only when
///   <paramref name="Kind"/> is <c>"choice"</c> (e.g. every shipped ∪ owner theme, slug plus
///   display name, for <c>Station:Theme</c> — PLAN T183) — lets a client render a <c>&lt;select&gt;</c> instead of a text
///   box, with a real display label rather than a raw slug, so a typo cannot produce an
///   unresolvable value (SPEC F102.14). <see langword="null"/> for every other kind.
/// </param>
public sealed record SettingDto(
    string Key,
    string Value,
    string Source,
    string ApplyMode,
    string Kind,
    string Unit,
    IReadOnlyList<SettingChoice>? Choices = null);
