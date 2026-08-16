namespace GenWave.Host.Api;

/// <summary>
/// One row on <c>GET /api/icon-packs</c> (SPEC F130.4/F130.5, STORY-337, PLAN T303/T304) — an
/// installed icon pack. <see cref="Definition"/> DOES ride this listing wire, unlike
/// <c>AvatarPackSummaryDto</c>/<c>FontLibraryPackDto</c>'s own "no bytes on the listing" posture
/// (PLAN T304 rider, review-discussed): those two kinds withhold BINARY assets from their own
/// listings (the N+1-with-bytes lesson, PLAN T294) — an icon pack carries no binary assets at all
/// (SPEC F130.6), only its own already-canonical, ≤256 KiB, whitelist-safe JSON text (the SAME
/// text <see cref="IconPackController.Active"/> already serves for the one currently-active pack).
/// Riding it here is what lets the Wardrobe Icons tab (PLAN T304) draw a real specimen row per
/// installed pack — through the admin-ui's own defensive safe renderer, never trusted blindly even
/// though this station's own <c>Install</c> route already validated it once.
///
/// <para>
/// <b>NO <c>Name</c> FIELD (unlike <see cref="AvatarPackSummaryDto.Name"/>) — SPEC F130.1's own
/// <c>gw-icon-pack</c> document has no pack-level display-name field at all: a style block plus an
/// icons map, nothing else. <see cref="Slug"/> IS the only honest label this schema can offer (the same
/// reasoning <c>StationSettingsAllowlist.IconPackChoices</c>'s own remarks give for why a
/// <c>Station:IconPack</c> choice's label is its slug too) — the Wardrobe Icons tab renders it as-is,
/// never inventing a display name this store was never handed.
/// </b>
/// </para>
/// </summary>
/// <param name="Slug">The catalog entry's own slug this pack installed from (SPEC F130.5) — unique
/// across every installed pack.</param>
/// <param name="IconCount">The number of icon names the stored definition declares — a cheap
/// <c>icons</c> object KEY COUNT (PLAN T304 review rider 7), never a full
/// <see cref="Icons.IconPackDefinitionParser.Validate"/> re-walk (this listing enumerates every
/// installed pack at once; the expensive whitelist/grammar gate belongs to the one-time install
/// write and the single-pack <see cref="IconPackController.Active"/> read, not a repeated per-row
/// listing cost) — degrading to <c>0</c>, never a 500, on the (should-never-happen) chance the
/// stored value is not even parseable JSON.</param>
/// <param name="Definition">The stored, already-canonical <c>gw-icon-pack</c> document (PLAN T304) —
/// see this record's own class remarks for why this rides the listing wire, unlike a binary-asset
/// kind's own summary DTO.</param>
/// <param name="ImportedFrom">Provenance stamp (db/25 pattern) — always equal to <see cref="Slug"/>
/// today (a pack has no authored-in-place path).</param>
/// <param name="ImportedAt">When this pack was last (re)installed.</param>
public sealed record IconPackSummaryDto(string Slug, int IconCount, string Definition, string ImportedFrom, DateTime ImportedAt);
