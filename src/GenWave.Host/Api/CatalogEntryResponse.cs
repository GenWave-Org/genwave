namespace GenWave.Host.Api;

/// <summary>
/// 200 response body for <c>GET /api/catalog/entries/{slug}</c> (SPEC F90.2, F90.3, F90.4a) once a
/// real entry is being served. <see cref="Card"/>/<see cref="Meta"/> stay the RAW hash-verified
/// JSON text — the same <c>&lt;slug&gt;.persona.json</c>/<c>&lt;slug&gt;.meta.json</c> bytes a
/// hand-downloaded copy would carry (SPEC F90.5: the review-then-import flow deserializes the card
/// itself at import time through the EXISTING F79 import endpoint — this endpoint never duplicates
/// that parsing).
///
/// <para>
/// <see cref="Card"/> KEEPS ITS PERSONA-ERA NAME DELIBERATELY (F103.2 review call, T176): the
/// internal entry model generalises its two-file fields from persona-specific <c>card</c> to the
/// neutral <c>manifest</c> (<see cref="Catalog.CatalogEntrySummary.Manifest"/>,
/// <see cref="Catalog.CatalogEntryContent.ManifestJson"/>), but THIS wire field is what the Admin
/// UI's existing persona-review screen already reads — renaming it would be a second, UI-side
/// change this backend-only task has no business making. Keeping the wire name is the smaller,
/// lower-blast-radius option (the alternative — updating the admin-ui read in the same task — would
/// reach outside this task's owned files for zero behavioural gain); a future kind-aware DTO
/// (once a theme detail view actually exists) is exactly the additive, kind-routed follow-up F103.3
/// anticipates, not a rename of this one.
/// </para>
///
/// <see cref="Kind"/>/<see cref="Audience"/>/<see cref="BestFor"/>/<see cref="Author"/>/
/// <see cref="Description"/>/<see cref="SamplePatter"/> are read straight off the hash-verified
/// <see cref="CatalogEntryContent"/> this whole response is built from (<see cref="Kind"/>/
/// <see cref="Audience"/>/<see cref="BestFor"/> straight off it; <see cref="Author"/>/
/// <see cref="Description"/>/<see cref="SamplePatter"/> parsed out of <see cref="Meta"/> via
/// <see cref="Catalog.CatalogEntryMetaJson"/>). See <see cref="CatalogController"/>'s own remarks
/// for the shared <see cref="Unreachable"/>-flag shape this reuses from <see cref="CatalogIndexResponse"/>.
/// </summary>
/// <param name="Card">The entry's manifest — a persona card today — as raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Meta">The shelf metadata's raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="FetchedAt">When THIS content was originally fetched (SPEC F90.4's stale-serve stamp); <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Unreachable">
/// <see langword="true"/> when the catalog itself is currently unreachable (no usable index to
/// resolve this slug against) — distinct from a genuinely unknown slug, which 404s instead.
/// </param>
/// <param name="Kind">
/// <c>"persona"</c>, <c>"theme"</c>, or <c>"font"</c> (SPEC F103.1, F103.3, widened by F104.1), or
/// <see langword="null"/> when <see cref="Unreachable"/>. A font entry's <see cref="Card"/>/
/// <see cref="Meta"/> are its raw <c>.font.json</c>/<c>.meta.json</c> text — the SAME generic,
/// kind-agnostic fetch every other kind already gets (S1 review finding, T193): this endpoint
/// never builds a font-specific projection (no asset list on the wire yet), it only had to stop
/// 500ing on <see cref="Kind"/> itself.
/// </param>
/// <param name="Audience"><c>"everyone"</c> or <c>"mature"</c> (same lowercase wire vocabulary as <see cref="CatalogShelfEntryDto.Audience"/>), or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="BestFor">Optional genre chips (F90.4a), or <see langword="null"/> when <see cref="Unreachable"/> — empty, never null, once reachable.</param>
/// <param name="Author">The entry's credited author (F90.4a), or <see langword="null"/> when unreachable or absent from meta.json.</param>
/// <param name="Description">The entry's shelf description (F90.4a), or <see langword="null"/> when unreachable or absent from meta.json.</param>
/// <param name="SamplePatter">Sample patter lines (F90.4a), or <see langword="null"/> when <see cref="Unreachable"/> — empty, never null, once reachable.</param>
/// <param name="FontFamily">
/// A font entry's family name (SPEC F104.3, T194) — parsed from <see cref="Card"/> (the pack's own
/// <c>.font.json</c> manifest, already fetched and hash-verified to build this same response, via
/// the hardened <see cref="Catalog.CatalogFontManifestSerializer.Deserialize"/>) at ZERO extra
/// network cost. <see langword="null"/> for every non-font entry, when unreachable, or when the
/// manifest fails to parse (degrades — never a 500, see that method's own remarks).
/// </param>
/// <param name="FontByteTotal">A font entry's summed asset bytes (mirrors <see cref="CatalogShelfEntryDto.FontByteTotal"/>'s own computation) — <see langword="null"/> for every non-font entry or when unreachable.</param>
/// <param name="FontSpecimenFile">
/// The bare filename of the pack's upright face (SPEC F104.4's "the real face") — resolved by
/// cross-referencing the manifest's own <c>files[]</c> <c>role:"upright"</c> entry against this
/// entry's OWN hash-verified <see cref="Catalog.CatalogEntrySummary.Assets"/> list, so it only ever
/// names something <c>GET /api/catalog/entries/{slug}/assets/{file}</c> can actually serve. Pass it
/// straight to that route to render the transient specimen preview. <see langword="null"/> for every
/// non-font entry, when unreachable, or when no upright face resolves.
/// </param>
/// <param name="FontLicense">
/// A font entry's licence identifier (PLAN T204, Dean's post-v3.1.0 review: the pre-install review
/// panel showed no licence at all) — parsed off the SAME hash-verified <see cref="Card"/> manifest
/// <see cref="FontFamily"/> already reads, via the same <see cref="Catalog.CatalogFontManifestSerializer.Deserialize"/>
/// call, zero extra cost. Paired with <see cref="FontVersion"/>/<see cref="FontSubset"/> to mirror the
/// admin UI's Wardrobe page's own "licence · version · subset" line (<c>FontLibraryPackDto</c> — the
/// wire DTO keeps its pre-rename name; only the UI label/route became "Wardrobe") so the SAME trust
/// fact reads identically whether an operator is reviewing a pack pre-install or inspecting one
/// already installed. <see langword="null"/> for every non-font entry, when unreachable, or when the
/// manifest fails to parse (degrades — never a 500, see that method's own remarks).
/// </param>
/// <param name="FontVersion">A font entry's manifest version (PLAN T204) — genuinely optional even on a cleanly-parsed manifest (<see cref="Catalog.CatalogFontManifest.Version"/>'s own shape); <see langword="null"/> for every non-font entry, when unreachable, or when absent/unparseable.</param>
/// <param name="FontSubset">A font entry's manifest subset, e.g. <c>"latin"</c> (PLAN T204) — <see langword="null"/> for every non-font entry, when unreachable, or when the manifest fails to parse.</param>
public sealed record CatalogEntryResponse(
    string? Card,
    string? Meta,
    DateTimeOffset? FetchedAt,
    bool Unreachable,
    string? Kind,
    string? Audience,
    IReadOnlyList<string>? BestFor,
    string? Author,
    string? Description,
    IReadOnlyList<string>? SamplePatter,
    string? FontFamily,
    long? FontByteTotal,
    string? FontSpecimenFile,
    string? FontLicense,
    string? FontVersion,
    string? FontSubset);
