namespace GenWave.Host.Configuration;

/// <summary>
/// Metadata for a single operator-editable configuration key.
/// </summary>
/// <param name="Key">
/// The <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> key (colon-separated,
/// e.g. <c>"Loudness:TargetLufs"</c>). Must not overlap with any secret key.
/// </param>
/// <param name="ApplyMode">
/// Whether the new value takes effect immediately (<see cref="SettingApplyMode.Live"/>), only after
/// the engine is restarted (<see cref="SettingApplyMode.EngineRestart"/>), or only the next time a
/// file is (re-)analyzed (<see cref="SettingApplyMode.Enrichment"/>).
/// </param>
/// <param name="Kind">
/// The UI input kind — <see cref="SettingKind.Boolean"/> renders a checkbox;
/// <see cref="SettingKind.Number"/> renders a numeric input.
/// </param>
/// <param name="Unit">
/// A short unit label for display next to the input (e.g. <c>"LUFS"</c>, <c>"seconds"</c>).
/// Empty string for booleans that carry no numeric unit.
/// </param>
public sealed record AllowedSetting(
    string Key,
    SettingApplyMode ApplyMode,
    SettingKind Kind,
    string Unit);
