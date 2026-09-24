using Microsoft.Extensions.Localization;

namespace GenWave.Host.Configuration;

/// <summary>
/// The one seam every consumer reads a setting's operator-facing copy through (SPEC F205.2,
/// STORY-477, PLAN T573) — labels, help text, group labels, and choice labels, all backed by
/// <see cref="IStringLocalizer{SettingsResources}"/> over <c>Configuration/SettingsResources.resx</c>
/// (<see cref="SettingsResources"/>'s own remarks explain how that binding resolves). T574 is the
/// only consumer today (the settings GET DTO); this task adds the seam without wiring it in.
/// </summary>
/// <remarks>
/// A missing resx entry (<see cref="LocalizedString.ResourceNotFound"/>) never throws and never
/// fails boot (AC14) — each accessor degrades to the most honest fallback available: the key
/// itself for a label, an empty string for help copy (silence, not a scary placeholder), the enum
/// member's own name for a group, and the raw value for a choice. A typo'd or forgotten resx entry
/// is a missing translation, not an outage.
/// </remarks>
public sealed class SettingCopy
{
    readonly IStringLocalizer<SettingsResources> localizer;

    public SettingCopy(IStringLocalizer<SettingsResources> localizer)
    {
        this.localizer = localizer;
    }

    /// <summary>The plain-English label for <paramref name="key"/> (resx entry <c>{key}.Label</c>) —
    /// falls back to <paramref name="key"/> itself.</summary>
    public string Label(string key) => Resolve($"{key}.Label", fallback: key);

    /// <summary>The help sentence for <paramref name="key"/> (resx entry <c>{key}.Help</c>) — falls
    /// back to an empty string.</summary>
    public string Help(string key) => Resolve($"{key}.Help", fallback: string.Empty);

    /// <summary>The admin UI section label for <paramref name="group"/> (resx entry
    /// <c>Group.{lowercase group name}.Label</c>) — falls back to the enum member's own name.</summary>
    public string GroupLabel(SettingGroup group)
    {
        var name = group.ToString();
        return Resolve($"Group.{name.ToLowerInvariant()}.Label", fallback: name);
    }

    /// <summary>The label for one <see cref="SettingKind.Choice"/> value of <paramref name="key"/>
    /// (resx entry <c>Choice.{key}.{value}</c>) — falls back to <paramref name="value"/> itself.</summary>
    public string ChoiceLabel(string key, string value) =>
        Resolve($"Choice.{key}.{value}", fallback: value);

    string Resolve(string name, string fallback)
    {
        var localized = localizer[name];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }
}
