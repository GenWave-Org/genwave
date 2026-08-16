namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/icon-packs/{slug}/install</c> (SPEC F130.5, STORY-337, PLAN
/// T303/T304) — the icon-kind sibling of <see cref="AvatarPackInstallResponse"/>/<see cref="FontPackInstallResponse"/>,
/// same "only what the accepted write genuinely hands back" posture. Narrower still than either sibling
/// — SPEC F130.1's <c>gw-icon-pack</c> document carries no pack-level display name at all (see
/// <see cref="IconPackSummaryDto"/>'s own remarks), so this carries <see cref="IconCount"/> in its
/// place: cheap proof the install landed a non-empty definition, without echoing the pack's full icon
/// name list back on a write response nothing reads it from.
///
/// <para>
/// <b>NO <c>ImportedFrom</c> (PLAN T304 review rider 7, dropped from the T303-shipped shape).</b> It
/// was always definitionally equal to <see cref="Slug"/> (SPEC F130.5: a pack has no authored-in-place
/// path, unlike a theme's own <c>?catalogSlug=</c> disambiguation) — the admin-ui's Icons tab (PLAN
/// T304) never reads it off THIS response; it reads provenance off <c>GET /api/icon-packs</c>'s own
/// <see cref="IconPackSummaryDto.ImportedFrom"/> instead, the listing route every OTHER Wardrobe tab
/// already sources its own provenance chip from.
/// </para>
/// </summary>
/// <param name="Slug">The route slug — the upsert key, and the catalog entry this pack installed
/// from.</param>
/// <param name="IconCount">The number of icon names <see cref="Icons.IconPackDefinition.Icons"/> just
/// stored (both contract and out-of-contract names counted — mirrors
/// <see cref="Icons.IconPackValidationResult.Valid.IgnoredNames"/>'s own "still valid, still stored"
/// posture).</param>
public sealed record IconPackInstallResponse(string Slug, int IconCount);
