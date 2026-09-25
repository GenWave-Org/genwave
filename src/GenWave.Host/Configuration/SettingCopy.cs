using Microsoft.Extensions.Localization;

namespace GenWave.Host.Configuration;

/// <summary>
/// The one seam every consumer reads a setting's operator-facing copy through (SPEC F205.2,
/// STORY-477, PLAN T573) — labels, help text, group labels, and choice labels, all backed by
/// <see cref="IStringLocalizer{SettingsResources}"/> over <c>Configuration/SettingsResources.resx</c>
/// (<see cref="SettingsResources"/>'s own remarks explain how that binding resolves). PLAN T574
/// wires this seam into <c>GET /api/settings</c>'s <see cref="GenWave.Host.Api.SettingDto"/>.
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
    public string GroupLabel(SettingGroup group) => Resolve($"Group.{GroupId(group)}.Label", fallback: group.ToString());

    /// <summary>The wire id for <paramref name="group"/> (SPEC F205.3, PLAN T574) —
    /// <paramref name="group"/>'s own member name, lowercased — matching the resx
    /// <c>Group.{id}.Label</c> naming convention <see cref="GroupLabel"/> resolves against.</summary>
    public string GroupId(SettingGroup group) => group.ToString().ToLowerInvariant();

    /// <summary>The label for one <see cref="SettingKind.Choice"/> value of <paramref name="key"/>
    /// (resx entry <c>Choice.{key}.{value}</c>) — falls back to <paramref name="value"/> itself. Use
    /// <see cref="TryChoiceLabel"/> instead when the caller needs to tell a real resx hit apart from
    /// this fallback (PLAN T574 — a Choice key whose vocabulary is live catalog data, e.g.
    /// <c>Station:Theme</c>, must keep ITS OWN label rather than fall back to the raw value).</summary>
    public string ChoiceLabel(string key, string value) => TryChoiceLabel(key, value) ?? value;

    /// <summary>The resx label for one <see cref="SettingKind.Choice"/> value of
    /// <paramref name="key"/> (resx entry <c>Choice.{key}.{value}</c>), or <see langword="null"/>
    /// when no such entry exists (PLAN T574) — lets a caller distinguish "the resx names this
    /// choice" from "this key's choices are catalog data the resx deliberately never enumerates"
    /// (see the resx's own file-level remarks on <c>Station:Theme</c>/<c>Station:IconPack</c>),
    /// which <see cref="ChoiceLabel"/>'s own value fallback cannot: a catalog-sourced label (e.g. a
    /// theme's display name) is never itself the raw <paramref name="value"/>, so that fallback
    /// alone cannot tell "found" apart from "missing".</summary>
    public string? TryChoiceLabel(string key, string value)
    {
        var localized = localizer[$"Choice.{key}.{value}"];
        return localized.ResourceNotFound ? null : localized.Value;
    }

    /// <summary>The label for a saved value missing from a live/catalog-sourced choice list (SPEC
    /// F205.7a, STORY-479, PLAN T580) — resx entry <c>Choice.NotFound</c>, a single KEY-AGNOSTIC
    /// format string (falls back to <c>"{0} (not found)"</c>) applied to EVERY such value, distinct
    /// from the per-key <c>Choice.{key}.{value}</c> entries <see cref="TryChoiceLabel"/> resolves —
    /// the list a saved value is missing from is never itself part of the vocabulary a translator
    /// enumerates ahead of time.</summary>
    public string NotFoundLabel(string value) =>
        string.Format(Resolve("Choice.NotFound", fallback: "{0} (not found)"), value);

    string Resolve(string name, string fallback)
    {
        var localized = localizer[name];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }
}
