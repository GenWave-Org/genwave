namespace GenWave.Host.Api;

/// <summary>
/// One row on <c>GET /api/avatar-packs</c> (SPEC F128.3, STORY-332, PLAN T294) — an installed avatar
/// pack, metadata only (NO item bytes on this wire — mirrors <c>FontLibraryPackDto</c>'s own "listing
/// has no use for a face's raw payload" posture, applied to the avatar kind). <see cref="Slug"/>/
/// <see cref="ImportedFrom"/>/<see cref="ImportedAt"/> read straight off their own
/// <see cref="GenWave.Core.Domain.AvatarPack"/> store columns; <see cref="Name"/> exists only inside
/// the pack's stored <c>definition</c> manifest jsonb, so <see cref="Api.AvatarPackController.List"/>
/// parses it back out via the hardened
/// <see cref="Catalog.CatalogAvatarPackManifestSerializer.Deserialize"/> — the SAME parser
/// <see cref="Api.AvatarPackController.Install"/> already trusted once at write time — degrading to
/// <see langword="null"/>, never a 500, on the (should-never-happen) chance a stored
/// <c>definition</c> fails to re-parse (mirrors <c>FontLibraryPackDto.License</c>'s own degrade
/// posture).
///
/// <para>
/// <b>UNBOUNDED DISPLAY STRINGS (PLAN T294 rider 2).</b> Neither <see cref="Name"/> nor any
/// <see cref="AvatarPackSummaryItemDto.Name"/> carries a length bound on THIS read —
/// <see cref="Api.AvatarPackController"/>'s own install-time gates
/// (<see cref="Api.AvatarPackController.IsValidItemName"/>) bound an ITEM's name at write time, but
/// <see cref="Name"/> itself (the pack's own <c>packName</c>) is never bounded anywhere in that
/// controller. The Admin UI's Wardrobe Avatars tab (PLAN T294, this DTO's one real consumer) clamps
/// both for display — layout protection only, never a security boundary (React already escapes every
/// character it renders).
/// </para>
/// </summary>
/// <param name="Slug">The catalog entry's own slug this pack installed from (SPEC F128.3) — unique
/// across every installed pack.</param>
/// <param name="Name">The pack's own manifest <c>packName</c> — <see langword="null"/> only if the
/// stored <c>definition</c> fails to re-parse.</param>
/// <param name="Items">Every item this pack ships — name and suggestion only, see
/// <see cref="AvatarPackSummaryItemDto"/>'s own remarks for why no bytes ride this wire.</param>
/// <param name="ImportedFrom">Provenance stamp (db/25 pattern) — always equal to <see cref="Slug"/>
/// today (a pack has no authored-in-place path).</param>
/// <param name="ImportedAt">When this pack was last (re)installed.</param>
public sealed record AvatarPackSummaryDto(
    string Slug,
    string? Name,
    IReadOnlyList<AvatarPackSummaryItemDto> Items,
    string ImportedFrom,
    DateTime ImportedAt);
