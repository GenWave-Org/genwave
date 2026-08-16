namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.avatar_pack</c> by <see cref="Abstractions.IAvatarPackStore.GetAllAsync"/>
/// (SPEC F128, STORY-332, PLAN T294, review finding B1) — a Dean-curated avatar pack installed from the
/// Community Catalog's <c>avatar</c> kind, WITH every one of its items' metadata (name + suggested
/// persona), but NEVER their bytes — see <see cref="AvatarPackItemSummary"/>'s own remarks for why that
/// type is structurally incapable of lying about it, unlike a shared <see cref="AvatarPack"/> shape
/// reused with an empty or bytes-stripped item list would have been. Mirrors <see cref="FontPack"/>'s
/// own "one listing shape, metadata-only faces" precedent, applied where a SECOND read
/// (<see cref="Abstractions.IAvatarPackStore.GetBySlugAsync"/>) also needs the bytes-carrying shape a
/// font pack has no equivalent second read for.
/// </summary>
/// <param name="Slug">The catalog entry's own slug — unique across every installed pack (the table's
/// <c>UNIQUE(slug)</c> constraint), mirrors <see cref="AvatarPack.Slug"/>.</param>
/// <param name="Definition">The stored <c>definition</c> column, verbatim jsonb text — a caller
/// (GenWave.Host, downstream of this GenWave.Core seam) (de)serializes at its own edge, mirrors
/// <see cref="AvatarPack.Definition"/>.</param>
/// <param name="ImportedFrom">Provenance stamp — the catalog entry's slug this pack installed from,
/// mirrors <see cref="AvatarPack.ImportedFrom"/>.</param>
/// <param name="ImportedAt">The moment this pack was last (re)installed.</param>
/// <param name="Items">Every item this pack ships, name + suggested persona only — see
/// <see cref="AvatarPackItemSummary"/>'s own remarks.</param>
public sealed record AvatarPackSummary(
    string Slug,
    string Definition,
    string ImportedFrom,
    DateTime ImportedAt,
    IReadOnlyList<AvatarPackItemSummary> Items);
