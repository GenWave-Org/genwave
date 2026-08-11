namespace GenWave.Host.Shows;

/// <summary>
/// A show import manifest's parsed, trusted content (SPEC F118.1/F118.2, PLAN T254) — the
/// <c>&lt;slug&gt;.show.json</c> shape genwave-catalog's own <c>show-manifest.schema.json</c> pins:
/// <see cref="Name"/>/<see cref="Tagline"/>/<see cref="Flavor"/>, the exact three fields
/// <see cref="Core.Domain.Show"/> itself carries as authored content.
///
/// <para>
/// NO EMBEDDED <c>slug</c> — deliberately, mirrors <see cref="Core.Domain.PersonaCard"/>'s own shape,
/// NOT <see cref="Theming.ThemeManifest"/>'s. A show-manifest document has no <c>slug</c> property at
/// all (unlike a theme manifest), so <see cref="Api.ShowsController.Import"/> never has an
/// embedded-vs-route slug mismatch to resolve or re-stamp — the route slug is the ONLY slug in play,
/// silently, the same way it already is for a persona-card import.
/// </para>
///
/// <para>
/// <see cref="Name"/>/<see cref="Flavor"/> are already run through
/// <see cref="Context.ContextFactSanitizer"/> by the time <see cref="ShowManifestParser.Parse"/>
/// returns one of these — see that method's own remarks for why (the T249-recorded prompt-injection
/// constraint). <see cref="Tagline"/> is untouched, raw manifest text — it never reaches an LLM
/// prompt, only the admin editor and the spectator disclosure surface (SPEC F115.3).
/// </para>
/// </summary>
public sealed record ShowManifest(string Name, string Tagline, string Flavor);
