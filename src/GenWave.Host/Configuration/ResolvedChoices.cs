namespace GenWave.Host.Configuration;

/// <summary>
/// One allowlisted <see cref="SettingKind.Choice"/>/<see cref="SettingKind.MultiChoice"/> key's resolved, localized choice list for one
/// request (SPEC F205.7, STORY-479, PLAN T580) — <see cref="ISettingChoiceResolver.ResolveAsync"/>'s
/// per-key result. <see cref="GenWave.Host.Api.SettingsController"/>'s <c>BuildDto</c> reads this
/// straight onto <see cref="GenWave.Host.Api.SettingDto.Choices"/>/
/// <see cref="GenWave.Host.Api.SettingDto.ChoicesStale"/>/<see cref="GenWave.Host.Api.SettingDto.ChoicesFailed"/>
/// without itself knowing whether the entry's source was Static, Catalog, or Probe.
///
/// Public, like <see cref="ISettingChoiceResolver"/> itself: it is that public interface's own
/// return type, so it must be at least as accessible.
/// </summary>
/// <param name="Choices">
/// The choices to present — the saved value, if missing from what the source resolved, is already
/// appended (SPEC F205.7a; see <see cref="SettingChoiceResolver"/>'s own remarks).
/// </param>
/// <param name="Stale">
/// True when a live source's most recent attempt failed but an earlier attempt for the SAME scope
/// succeeded — <see cref="Choices"/> is that earlier list (SPEC F205.7c).
/// </param>
/// <param name="Failed">
/// True when a live source has no successful attempt for the current scope to fall back on —
/// <see cref="Choices"/> carries only the appended saved value, if any (SPEC F205.7c).
/// </param>
public sealed record ResolvedChoices(
    IReadOnlyList<SettingChoice> Choices,
    bool Stale = false,
    bool Failed = false);
