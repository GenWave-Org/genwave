namespace GenWave.Host.Configuration;

/// <summary>
/// Marker type for <see cref="Microsoft.Extensions.Localization.IStringLocalizer{TResource}"/>
/// resolution against <c>Configuration/SettingsResources.resx</c> (SPEC F205.2, STORY-477, PLAN
/// T573) — the one resx backing every setting's label/help/group/choice copy. Never instantiated
/// or called; its only job is giving
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{TResource}"/> a stable type
/// identity to key resource lookups off, the same "dummy marker class" shape the ASP.NET Core
/// docs' own <c>SharedResource</c> sample uses
/// (https://learn.microsoft.com/aspnet/core/fundamentals/localization/make-content-localizable#shared-resources).
///
/// <para>
/// The resx file is named after this type — <c>SettingsResources.resx</c>, with any satellite as
/// <c>SettingsResources.{culture}.resx</c> — because
/// <see cref="Microsoft.Extensions.Localization.ResourceManagerStringLocalizerFactory"/> resolves
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{TResource}"/> by computing a
/// manifest resource name from <em>this type's</em> full name and matching it against whatever
/// the SDK's default resx build already embedded — no <c>LogicalName</c> override, no
/// <c>[ResourceLocation]</c>/<c>[RootNamespace]</c> attribute needed, since the file already lives
/// at the namespace-matching path (<c>Configuration/</c>) and the project's root namespace already
/// matches its assembly name
/// (https://learn.microsoft.com/aspnet/core/fundamentals/localization/provide-resources#resource-file-naming).
/// A mismatched file name (this project shipped one for a while, <c>Settings.resx</c>) still
/// compiles and embeds — MSBuild doesn't require the two names to agree — but
/// <see cref="Microsoft.Extensions.Localization.ResourceManagerStringLocalizerFactory"/>'s own
/// satellite-culture matching keys off the base manifest name, and a <c>LogicalName</c> override
/// that pins only the neutral resource's identity leaves each culture-suffixed satellite assembly
/// under its own physical-filename-derived name instead — so a shipped
/// <c>Settings.fr.resx</c> resolved to a resource name with no <c>fr</c> suffix, and `fr` silently
/// fell back to en. Matching the file name to the type name sidesteps the whole class of bug.
/// </para>
/// </summary>
public sealed class SettingsResources
{
}
