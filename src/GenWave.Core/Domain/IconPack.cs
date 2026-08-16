namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.icon_pack</c> (SPEC F130, STORY-337, PLAN T290) — a
/// Dean-curated icon pack installed from the Community Catalog's <c>icon</c> kind. Pure jsonb, no
/// binary assets (F130.1's constrained vector document — a geometry-primitive whitelist that cannot
/// express script, mirroring <see cref="OwnerTheme"/>'s own opaque-<see cref="Definition"/> discipline
/// rather than <see cref="FontPack"/>/<see cref="AvatarPack"/>'s own bytea-backed shape).
/// </summary>
/// <param name="Slug">The catalog entry's own slug — unique across every installed pack (the table's
/// <c>UNIQUE(slug)</c> constraint).</param>
/// <param name="Definition">The stored <c>definition</c> column, verbatim jsonb text — the constrained
/// vector document a caller (GenWave.Host, downstream of this GenWave.Core seam) parses at its own
/// edge.</param>
/// <param name="ImportedFrom">Provenance stamp — the catalog entry's slug this pack installed from.
/// Never <see langword="null"/> — a pack has no authored-in-place path, the catalog install route is
/// the only door (mirrors <see cref="FontPack.ImportedFrom"/>'s own non-nullable contract).</param>
/// <param name="ImportedAt">The moment this pack was last (re)installed.</param>
public sealed record IconPack(
    string Slug,
    string Definition,
    string ImportedFrom,
    DateTime ImportedAt);
