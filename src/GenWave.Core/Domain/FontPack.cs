namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.font_pack</c>, with every one of its
/// <c>station.font_pack_face</c> rows folded in (SPEC F104, STORY-282, PLAN T198) — a Dean-curated
/// font pack installed from the Community Catalog's <c>font</c> kind. <see cref="Definition"/> is
/// the raw jsonb text: the catalog pack manifest recorded at install time. Deliberately opaque here
/// rather than typed as <c>GenWave.Host.Catalog.CatalogFontManifest</c> — that type lives in
/// <c>GenWave.Host</c>, downstream of this <c>GenWave.Core</c> seam, so a caller reconstitutes it at
/// its own edge, exactly the way <see cref="OwnerTheme"/>'s own remarks describe for
/// <c>station.theme</c>.
/// </summary>
/// <param name="Slug">The catalog entry's own slug — unique across every installed pack (the
/// table's <c>UNIQUE(slug)</c> constraint).</param>
/// <param name="Family">The CSS family name this pack's faces render under, e.g. <c>"Space Grotesk"</c>.</param>
/// <param name="Definition">The stored <c>definition</c> column, verbatim jsonb text.</param>
/// <param name="ImportedFrom">Provenance stamp (mirrors <c>station.persona</c>'s db/25 precedent and
/// <see cref="OwnerTheme.ImportedFrom"/>): the catalog entry's slug this pack installed from. Unlike
/// a theme, never <see langword="null"/> — a pack has no authored-in-place path, the catalog install
/// route is the only door.</param>
/// <param name="ImportedAt">The moment this pack was last (re)installed.</param>
/// <param name="CreatedAt">When this row was first inserted.</param>
/// <param name="Faces">Every face this pack ships (SPEC F104: one family, 1-2 faces — upright and an
/// optional italic), metadata only (see <see cref="FontPackFace"/>'s own remarks) — in no
/// particular guaranteed order.</param>
public sealed record FontPack(
    string Slug,
    string Family,
    string Definition,
    string ImportedFrom,
    DateTime ImportedAt,
    DateTime CreatedAt,
    IReadOnlyList<FontPackFace> Faces);
