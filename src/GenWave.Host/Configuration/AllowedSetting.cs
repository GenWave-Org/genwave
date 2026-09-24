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
/// <param name="Group">
/// The admin UI section this key is grouped under (SPEC F205.1, STORY-477, PLAN T572) — a pure
/// display/organization concern, unrelated to <paramref name="ApplyMode"/>/<paramref name="Kind"/>/
/// validation. Required (no default) so every one of <see cref="StationSettingsAllowlist.All"/>'s
/// entries states its group explicitly — a call site that omits it fails to compile, the structural
/// half of AC2's "every key has a group" guarantee.
/// </param>
/// <param name="Choices">
/// The closed set of valid <see cref="SettingChoice"/> value/label pairs for a
/// <see cref="SettingKind.Choice"/> entry (e.g. every shipped ∪ owner theme, slug plus display
/// name — PLAN T183).
/// <see langword="null"/> for every other <see cref="SettingKind"/> — a
/// <see cref="SettingChoice.Value"/> outside this set is a <see cref="SettingValidator"/>
/// rejection, never a silently-unresolvable typo; a <see cref="SettingChoice.Label"/> is never
/// itself an acceptable input.
/// </param>
/// <param name="Min">
/// The inclusive (or, for a validator shape with an exclusive floor, the excluded) lower bound for a
/// <see cref="SettingKind.Number"/> entry (SPEC F205.1, PLAN T572 — moved off <see cref="SettingValidator"/>'s
/// own <c>internal const</c> fields onto this record, the single source of truth both the validator's
/// per-key check and its 400 message now read). <see langword="null"/> for every non-<see cref="SettingKind.Number"/>
/// entry, and for the two <see cref="SettingKind.NumberList"/> keys (not a Number key — left alone by
/// this task).
/// </param>
/// <param name="Max">
/// The inclusive upper bound for a <see cref="SettingKind.Number"/> entry — see <paramref name="Min"/>.
/// </param>
public sealed record AllowedSetting(
    string Key,
    SettingApplyMode ApplyMode,
    SettingKind Kind,
    string Unit,
    SettingGroup Group,
    IReadOnlyList<SettingChoice>? Choices = null,
    double? Min = null,
    double? Max = null)
{
    /// <summary>
    /// Where a <see cref="SettingKind.Choice"/> entry's selectable values come from (SPEC F205.1).
    /// Defaults to <see cref="SettingChoiceSource.Static"/> — every entry today, since
    /// <see cref="SettingChoiceSource.Catalog"/> and <see cref="SettingChoiceSource.Probe"/> are
    /// declared but unused until SPEC F205.7 wires a consumer — so no call site needs to state it.
    /// </summary>
    public SettingChoiceSource ChoiceSource { get; init; } = SettingChoiceSource.Static;
}
