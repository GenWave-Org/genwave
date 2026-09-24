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
/// <param name="Label">
///   The plain-English label for this key (SPEC F205.3, STORY-477, PLAN T574), resolved for the
///   request culture via <see cref="Configuration.SettingCopy.Label"/> — falls back to
///   <see cref="Key"/> itself when the resx carries no entry (never empty).
/// </param>
/// <param name="Help">
///   The operator-facing help sentence for this key, resolved for the request culture via
///   <see cref="Configuration.SettingCopy.Help"/> — falls back to an empty string when the resx
///   carries no entry.
/// </param>
/// <param name="Group">
///   The admin UI section this key is grouped under (SPEC F205.1/F205.3), with its label resolved
///   for the request culture — see <see cref="SettingGroupDto"/>.
/// </param>
/// <param name="Min">
///   The inclusive lower bound for a <see cref="Kind"/> of <c>"number"</c>, carried straight off
///   <see cref="Configuration.AllowedSetting.Min"/> — <see langword="null"/> for every other kind.
/// </param>
/// <param name="Max">
///   The inclusive upper bound for a <see cref="Kind"/> of <c>"number"</c>, carried straight off
///   <see cref="Configuration.AllowedSetting.Max"/> — <see langword="null"/> for every other kind.
/// </param>
/// <param name="Choices">
///   The closed set of valid <see cref="SettingChoice"/> value/label pairs, present only when
///   <paramref name="Kind"/> is <c>"choice"</c> (e.g. every shipped ∪ owner theme, slug plus
///   display name, for <c>Station:Theme</c> — PLAN T183) — lets a client render a <c>&lt;select&gt;</c> instead of a text
///   box, with a real display label rather than a raw slug, so a typo cannot produce an
///   unresolvable value (SPEC F102.14). Each choice's label is resolved for the request culture
///   when the resx carries a <c>Choice.{Key}.{value}</c> entry for it (PLAN T574); a Choice key
///   whose vocabulary is live catalog data instead (<c>Station:Theme</c>, <c>Station:IconPack</c>)
///   keeps its own catalog-sourced label unchanged. <see langword="null"/> for every other kind.
/// </param>
/// <param name="Version">
///   Optimistic-concurrency token (gh-#486): the key's currently stored version, or <c>0</c> when no
///   override row exists yet (<paramref name="Source"/> is <c>"default"</c>). A client that wants a
///   later <c>PUT /api/settings</c> write to this key guarded against a concurrent editor's save
///   echoes this back as that update's <see cref="SettingUpdateRequest.ExpectedVersion"/>; omitting
///   it keeps the pre-gh-#486 unconditional last-write-wins behavior.
/// </param>
public sealed record SettingDto(
    string Key,
    string Value,
    string Source,
    string ApplyMode,
    string Kind,
    string Unit,
    string Label,
    string Help,
    SettingGroupDto Group,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<SettingChoice>? Choices = null,
    long Version = 0);
