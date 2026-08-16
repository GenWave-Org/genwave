namespace GenWave.Host.Api;

/// <summary>
/// One row on <c>GET /api/icon-packs</c> (SPEC F130.4/F130.5, STORY-337, PLAN T303) — an installed icon
/// pack, metadata only (the definition itself, needed by <c>GET /api/icon-packs/active</c> only for the
/// currently ACTIVE pack, never rides this listing wire — the same "listing has no use for the full
/// payload" posture <c>AvatarPackSummaryDto</c>/<c>FontLibraryPackDto</c> already carry for their own
/// kinds).
///
/// <para>
/// <b>NO <c>Name</c> FIELD (unlike <see cref="AvatarPackSummaryDto.Name"/>) — SPEC F130.1's own
/// <c>gw-icon-pack</c> document has no pack-level display-name field at all: a style block plus an
/// icons map, nothing else. <see cref="Slug"/> IS the only honest label this schema can offer (the same
/// reasoning <c>StationSettingsAllowlist.IconPackChoices</c>'s own remarks give for why a
/// <c>Station:IconPack</c> choice's label is its slug too) — the Wardrobe Icons tab (a future task)
/// renders it as-is, never inventing a display name this store was never handed.
/// </b>
/// </para>
/// </summary>
/// <param name="Slug">The catalog entry's own slug this pack installed from (SPEC F130.5) — unique
/// across every installed pack.</param>
/// <param name="IconCount">The number of icon names the stored definition declares — re-parsed off the
/// stored <c>definition</c> jsonb via <see cref="Icons.IconPackDefinitionParser.Validate"/>, degrading
/// to <c>0</c>, never a 500, on the (should-never-happen) chance it fails to re-parse (mirrors
/// <see cref="AvatarPackSummaryDto"/>'s own re-parse-and-degrade posture).</param>
/// <param name="ImportedFrom">Provenance stamp (db/25 pattern) — always equal to <see cref="Slug"/>
/// today (a pack has no authored-in-place path).</param>
/// <param name="ImportedAt">When this pack was last (re)installed.</param>
public sealed record IconPackSummaryDto(string Slug, int IconCount, string ImportedFrom, DateTime ImportedAt);
