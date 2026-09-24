namespace GenWave.Host.Configuration;

/// <summary>
/// Where an <see cref="AllowedSetting"/> of <see cref="SettingKind.Choice"/> sources its selectable
/// values from (SPEC F205.1, STORY-477, PLAN T572). A closed hierarchy — private base constructor,
/// sealed record cases — mirroring <see cref="GenWave.Core.Domain.FontPackDeleteResult"/>'s own shape
/// for the same exhaustive-switch guarantee.
///
/// <see cref="Static"/> is every entry's value today: the closed vocabulary is either the frozen
/// <see cref="AllowedSetting.Choices"/> list carried on the record itself, or — for
/// <c>Station:Theme</c>/<c>Station:IconPack</c> — a structural placeholder there, with
/// <see cref="GenWave.Host.Api.SettingsController"/> and <see cref="SettingValidator"/> both
/// re-sourcing the LIVE set from a DI-registered catalog instead (see
/// <see cref="StationSettingsAllowlist"/>'s own remarks). Nothing on the allowlist uses
/// <see cref="Catalog"/> or <see cref="Probe"/> yet — SPEC F205.7 (a later task) wires an entry's
/// choices to resolve from a named runtime source through this type instead of a frozen list;
/// declaring the shape now, unused, is deliberate (T572's own scope).
/// </summary>
public abstract record SettingChoiceSource
{
    private SettingChoiceSource() { }

    /// <summary>The singleton "no dynamic source" instance.</summary>
    public static SettingChoiceSource Static { get; } = new StaticSource();

    /// <summary>Choices are the record's own frozen <see cref="AllowedSetting.Choices"/> list.</summary>
    public sealed record StaticSource : SettingChoiceSource;

    /// <summary>Choices resolve from a named runtime catalog (F205.7 — unused until then).</summary>
    public sealed record Catalog(string Kind) : SettingChoiceSource;

    /// <summary>Choices resolve from a named runtime probe (F205.7 — unused until then).</summary>
    public sealed record Probe(string Name) : SettingChoiceSource;
}
