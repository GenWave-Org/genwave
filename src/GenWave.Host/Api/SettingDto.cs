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
///   <c>"number"</c> for numeric settings rendered as a number input.
/// </param>
/// <param name="Unit">
///   Short unit label for display (e.g. <c>"LUFS"</c>, <c>"seconds"</c>).
///   Empty string for booleans.
/// </param>
public sealed record SettingDto(
    string Key,
    string Value,
    string Source,
    string ApplyMode,
    string Kind,
    string Unit);
