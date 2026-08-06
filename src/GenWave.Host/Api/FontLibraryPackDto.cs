namespace GenWave.Host.Api;

/// <summary>
/// One row on <c>GET /api/fonts</c> (SPEC F104.7, STORY-284, PLAN T203) — an installed font pack,
/// metadata only (no <c>bytes</c> on this wire — mirrors <see cref="GenWave.Core.Abstractions.IFontPackStore.GetAllAsync"/>'s
/// own "listing has no use for a face's raw payload" posture). <see cref="Slug"/>/<see cref="Family"/>/
/// <see cref="Faces"/>/<see cref="ImportedFrom"/>/<see cref="ImportedAt"/> read straight off their own
/// <c>GenWave.Core.Domain.FontPack</c>/<c>FontPackFace</c> store columns; <see cref="License"/>/
/// <see cref="SourceUrl"/>/<see cref="Version"/>/<see cref="Subset"/> exist ONLY inside the pack's
/// stored <c>definition</c> manifest jsonb, so <see cref="Api.FontPackController"/>'s own <c>List</c>
/// action parses it back out via the hardened <see cref="Catalog.CatalogFontManifestSerializer.Deserialize"/>
/// — the SAME parser <c>Install</c> already trusted once at write time. All four degrade to
/// <see langword="null"/>, never a 500, on the (should-never-happen) chance a stored <c>definition</c>
/// fails to re-parse — <see cref="Slug"/>/<see cref="Family"/>/<see cref="Faces"/>/<see cref="ImportedFrom"/>/
/// <see cref="ImportedAt"/> are unaffected either way, since none of them round-trips through that parse.
///
/// <para>
/// <b>PLAIN TEXT ONLY (the T199/T200 stored-family/style obligation, closed here).</b>
/// <see cref="Family"/> and each <see cref="FontLibraryFaceDto.Style"/> are unbounded free-form prose
/// — <see cref="Catalog.CatalogFontManifestSerializer.Deserialize"/> only checks non-empty, never a
/// CSS-safe shape (see <see cref="Api.FontPackController"/>'s own remarks). This wire carries them
/// verbatim; the Admin UI's library page (PLAN T203) is this DTO's one real consumer, and renders both
/// as plain React text nodes ONLY — never interpolated into a stylesheet, inline <c>style</c> attribute,
/// or any other CSS context. Whichever consumer next reaches for either field in a CSS context (the
/// T206 editor pickers) still owns applying real CSS-injection-safe discipline first.
/// </para>
///
/// <para>
/// <b>SOURCEURL FORWARD OBLIGATION (T203 review finding N6, still open).</b> <see cref="SourceUrl"/>
/// is an UNVALIDATED string straight off the catalog's own manifest — <see cref="Catalog.CatalogFontManifestSerializer.Deserialize"/>
/// only checks it is non-empty, never that it is a well-formed URL, let alone an <c>https:</c> one
/// (mirrors this DTO's own family/style posture above). T203's library page never renders it as a
/// link today — it never appears in an <c>&lt;a href&gt;</c> anywhere in this codebase yet. Whichever
/// consumer is first to render <see cref="SourceUrl"/> as a clickable link (a plausible library-page
/// enhancement) MUST validate its scheme — <c>https:</c> only, rejecting <c>javascript:</c>,
/// <c>data:</c>, and any other scheme — before ever handing it to an <c>&lt;a href&gt;</c>; trusting
/// it as link-safe merely because it came from this store would be the exact class of mistake the
/// family/style obligation above already warns against, applied to a link target instead of CSS.
/// </para>
/// </summary>
/// <param name="Slug">The catalog entry's own slug this pack installed from (SPEC F104.5) — unique
/// across every installed pack.</param>
/// <param name="Family">The CSS family name this pack's faces render under, e.g. "Space Grotesk" —
/// rendered as plain text only, see this type's own remarks.</param>
/// <param name="Faces">Every face this pack ships, metadata only.</param>
/// <param name="License">The pack's licence identifier, e.g. "OFL-1.1" — <see langword="null"/> only
/// if the stored <c>definition</c> fails to re-parse.</param>
/// <param name="SourceUrl">Where the pack's upstream source lives — <see langword="null"/> only if the
/// stored <c>definition</c> fails to re-parse.</param>
/// <param name="Version">The upstream font's own version string, when the pack's manifest carries one
/// — genuinely optional even on a cleanly-parsed manifest (mirrors
/// <see cref="Catalog.CatalogFontManifest.Version"/>'s own <see langword="string?"/> shape).</param>
/// <param name="Subset">The pack's glyph subset, e.g. "latin" — <see langword="null"/> only if the
/// stored <c>definition</c> fails to re-parse.</param>
/// <param name="ImportedFrom">Provenance stamp (db/25 pattern, SPEC F104.7) — always equal to
/// <see cref="Slug"/> today (a pack has no authored-in-place path), read as its own column rather than
/// assumed identical, mirroring <c>GenWave.Core.Domain.FontPack.ImportedFrom</c>'s own remarks.</param>
/// <param name="ImportedAt">When this pack was last (re)installed.</param>
public sealed record FontLibraryPackDto(
    string Slug,
    string Family,
    IReadOnlyList<FontLibraryFaceDto> Faces,
    string? License,
    string? SourceUrl,
    string? Version,
    string? Subset,
    string ImportedFrom,
    DateTime ImportedAt);
