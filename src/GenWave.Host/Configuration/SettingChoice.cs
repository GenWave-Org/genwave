using GenWave.Host.Theming;

namespace GenWave.Host.Configuration;

/// <summary>
/// One valid value for a <see cref="SettingKind.Choice"/> setting, paired with the display label
/// the admin UI should render for it (T175, closing STORY-265's own review: the wire previously
/// carried bare slugs, forcing <c>ChoiceSettingControl</c> to either show a raw slug like
/// <c>cats-whisker</c> or invent a client-side prettifier that could drift from the manifest's own
/// <see cref="ThemeManifest.Name"/>).
/// </summary>
/// <param name="Value">
/// The value that is validated, stored (e.g. in <c>Station:Theme</c>), carried in the visitor
/// cookie, and resolved by <see cref="ThemeCatalog.Resolve"/> — for <see cref="ThemeManifest"/>-
/// backed choices, its <see cref="ThemeManifest.Slug"/>. A <see cref="Label"/> is never itself a
/// valid <see cref="Value"/>.
/// </param>
/// <param name="Label">
/// The human-readable display name (e.g. <c>"Cat's Whisker"</c>) — presentation only, never
/// accepted as an input by <see cref="SettingValidator"/>.
/// </param>
/// <param name="IsDefault">
/// True for the one choice (if any) a setting resolves to when its stored/staged value is the
/// empty string — for <c>Station:Theme</c>, the choice whose <see cref="Value"/> equals
/// <see cref="GenWave.Host.Theming.ThemeCatalog.ShippedDefaultSlug"/> (T175 follow-up: closes the
/// review finding that an empty <c>Station:Theme</c> row rendered as if <em>any</em> theme were
/// explicitly selected — usually whichever one happened to sort first — rather than as its own
/// distinct "unset" state). Defaults to <see langword="false"/>, which is also the correct answer
/// for every <see cref="SettingKind.Choice"/> setting whose closed set has no such "empty means
/// this" semantics — the admin UI degrades to a neutral "unset" label rather than assuming one
/// exists. A generic, kind-level fact (part of what makes a Choice-kind setting self-describing on
/// the wire), never a Theme-specific one, even though <c>Station:Theme</c> is the only consumer
/// today.
/// </param>
public sealed record SettingChoice(string Value, string Label, bool IsDefault = false);
