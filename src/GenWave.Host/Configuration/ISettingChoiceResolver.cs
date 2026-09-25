namespace GenWave.Host.Configuration;

/// <summary>
/// Resolves every allowlisted <see cref="SettingKind.Choice"/>/<see cref="SettingKind.MultiChoice"/> entry's presented choice list for
/// ONE request (SPEC F205.7, STORY-479, PLAN T580) — the seam <see cref="GenWave.Host.Api.SettingsController"/>'s
/// <c>Get</c>/<c>Put</c> each call exactly once, replacing the controller's own former
/// <c>ChoicesFor</c>/<c>LocalizedChoicesFor</c> pair. Implementations own the
/// Static/Catalog/Probe dispatch (<see cref="SettingChoiceSource"/>) and label localization; the
/// controller supplies only each key's CURRENT value, so a saved value missing from the resolved
/// list can still be appended rather than lost from the response (SPEC F205.7a).
///
/// <para>
/// Public (unlike its sibling <see cref="IChoiceProbe"/>/<see cref="ProbedChoiceCache"/>/
/// <see cref="SettingChoiceResolver"/>, all internal): <see cref="GenWave.Host.Api.SettingsController"/>
/// is itself public (ASP.NET Core's default controller discovery requires it), so its primary
/// constructor's <c>choiceResolver</c> parameter type must be at least as accessible as the
/// constructor — same reason <see cref="ResolvedChoices"/> is public too.
/// </para>
/// </summary>
public interface ISettingChoiceResolver
{
    /// <summary>
    /// Resolves the <see cref="SettingKind.Choice"/>/<see cref="SettingKind.MultiChoice"/> allowlist entries whose <see cref="AllowedSetting.Key"/>
    /// is present in <paramref name="currentValues"/> (case-insensitive, matching
    /// <see cref="StationSettingsAllowlist.ByKey"/>) — not every such entry on the allowlist (T580
    /// review finding F4): GET passes every stored/default value, so every choice-kind entry
    /// resolves, while PUT passes only the value(s) just written, so only those resolve. Each
    /// resolved key's current raw value is used to append a value missing from the resolved list
    /// rather than reject it (SPEC F205.7a); a key not present in <paramref name="currentValues"/> is
    /// not resolved at all.
    /// </summary>
    Task<IReadOnlyDictionary<string, ResolvedChoices>> ResolveAsync(
        IReadOnlyDictionary<string, string> currentValues, CancellationToken ct);
}
