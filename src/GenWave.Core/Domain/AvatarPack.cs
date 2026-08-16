namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.avatar_pack</c> (SPEC F128, STORY-332, PLAN T290) — a
/// Dean-curated avatar pack installed from the Community Catalog's <c>avatar</c> kind, mirroring
/// <see cref="FontPack"/>'s own shape (station.font_pack, db/32) almost verbatim. <see cref="Definition"/>
/// is the raw jsonb text: the catalog pack manifest recorded at install time — a caller (GenWave.Host,
/// downstream of this GenWave.Core seam) (de)serializes at its own edge, exactly the way
/// <see cref="FontPack.Definition"/>'s own remarks describe.
/// </summary>
/// <param name="Slug">The catalog entry's own slug — unique across every installed pack (the table's
/// <c>UNIQUE(slug)</c> constraint).</param>
/// <param name="Definition">The stored <c>definition</c> column, verbatim jsonb text.</param>
/// <param name="ImportedFrom">Provenance stamp — the catalog entry's slug this pack installed from.
/// Never <see langword="null"/> — a pack has no authored-in-place path, the catalog install route is
/// the only door (mirrors <see cref="FontPack.ImportedFrom"/>'s own non-nullable contract).</param>
/// <param name="ImportedAt">The moment this pack was last (re)installed.</param>
/// <param name="Items">Every item this pack ships, WITH bytes (unlike <see cref="FontPack.Faces"/>'s
/// own metadata-only listing shape) — see <see cref="AvatarPackItem"/>'s own remarks for why. Populated
/// by <see cref="Abstractions.IAvatarPackStore.GetBySlugAsync"/> (the one-pack detail read a later
/// apply-from-pack write, T295/T296, needs bytes for) — this type is that ONE bytes-carrying shape;
/// <see cref="Abstractions.IAvatarPackStore.GetAllAsync"/>'s own shelf-listing read returns the
/// separate, structurally bytes-free <see cref="AvatarPackSummary"/> instead (review finding B1 — a
/// shared shape reused with an empty or stripped item list could too easily drift back into carrying
/// bytes it has no business carrying).</param>
public sealed record AvatarPack(
    string Slug,
    string Definition,
    string ImportedFrom,
    DateTime ImportedAt,
    IReadOnlyList<AvatarPackItem> Items);
