namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/ad-packs/{slug}/install</c> (SPEC F162.2, STORY-393, PLAN T405) —
/// the ad-pack-kind sibling of <see cref="AvatarPackInstallResponse"/>, same "only what the accepted
/// write genuinely hands back" posture. No dedicated <c>station.ad_pack</c> row to summarize (unlike
/// every other pack-shaped kind): an ad-pack's install target IS <c>station.ad_brief</c>, the SAME
/// table an owner-authored brief lives in — <c>GET /api/ad-briefs</c> (already shipped, T403b) is
/// this kind's own full-detail read, not a new listing route this task adds.
///
/// <para>
/// NO <c>ImportedFrom</c> (T405 review fold, mirrors <see cref="IconPackInstallResponse"/>'s own T304
/// rider verbatim — dropped rather than kept the way <see cref="AvatarPackInstallResponse"/> does):
/// it is always definitionally equal to <see cref="Slug"/> (SPEC F162.2 — a pack has no
/// authored-in-place path), and no admin-ui consumer reads it off THIS response — unlike an avatar
/// pack, this kind has no dedicated per-pack listing route (<see cref="AdPackController"/>'s own class
/// remarks) for a provenance chip to source it from either, so the field would carry no reader on
/// EITHER wire at all.
/// </para>
/// </summary>
/// <param name="Slug">The route slug — the upsert key's <c>pack_slug</c> half, and the catalog entry
/// this pack installed from.</param>
/// <param name="PackName">The manifest's own OPTIONAL display pack name, or <see langword="null"/>
/// when the manifest declares none (SPEC F162.2's "pack metadata" is not a required field on this
/// kind — see <see cref="Catalog.CatalogAdPackManifest.PackName"/>'s own remarks).</param>
/// <param name="Brands">Every brand just upserted, in manifest order — mirrors
/// <see cref="AvatarPackInstallResponse.Items"/>'s own "the names just installed" shape.</param>
public sealed record AdPackInstallResponse(string Slug, string? PackName, IReadOnlyList<string> Brands);
