namespace GenWave.Host.Catalog;

/// <summary>
/// An ad-pack's <c>.ad-pack.json</c> manifest content (SPEC F162.2, STORY-393, PLAN T405) — mirrors
/// <see cref="CatalogAvatarPackManifest"/>'s own "ephemeral, hardened, null-tolerant" shape one kind
/// over, for the SIMPLEST pack yet: an OPTIONAL display <see cref="PackName"/> (the same "pack
/// metadata" SPEC F162.2's own words name, riding <see cref="Api.CatalogEntryResponse.PackName"/>
/// alongside an avatar pack's own — <see cref="Api.CatalogController.ToEntryResponse"/>'s own
/// remarks) plus its REQUIRED <see cref="Briefs"/> — data only, never a binary asset the index would
/// need to declare separately (this kind carries no <c>assets[]</c> arm at all, SPEC F162.2's own
/// "no audio assets" words).
///
/// <para>
/// NO CUSTOM <c>Equals</c>/<c>GetHashCode</c> (T405 review fold — dropped, unlike
/// <see cref="CatalogAvatarPackManifest"/>'s own structural-equality override one kind over): nothing
/// in this codebase ever compares two <see cref="CatalogAdPackManifest"/> instances for equality —
/// this type has no <c>Serialize</c> counterpart to round-trip against a golden fixture the way
/// <c>CatalogFontManifest</c>/<c>ThemeManifest</c> do, and no other caller needs list-value structural
/// equality either. The compiler-synthesized, reference-equality-over-<see cref="Briefs"/> default is
/// exactly as correct for every ACTUAL use this type has today (YAGNI) — a future caller that
/// genuinely needs structural equality can add it back then, with a real caller to justify it.
/// </para>
/// </summary>
public sealed record CatalogAdPackManifest(string? PackName, IReadOnlyList<CatalogAdPackBrief> Briefs);
