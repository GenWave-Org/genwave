namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/icon-packs/{slug}/install</c> (SPEC F130.5, STORY-337, PLAN T303) —
/// the icon-kind sibling of <see cref="AvatarPackInstallResponse"/>/<see cref="FontPackInstallResponse"/>,
/// same "only what the accepted write genuinely hands back" posture. Narrower still than either sibling
/// — SPEC F130.1's <c>gw-icon-pack</c> document carries no pack-level display name at all (see
/// <see cref="IconPackSummaryDto"/>'s own remarks), so this carries <see cref="IconCount"/> in its
/// place: cheap proof the install landed a non-empty definition, without echoing the pack's full icon
/// name list back on a write response nothing reads it from.
/// </summary>
/// <param name="Slug">The route slug — the upsert key, and the catalog entry this pack installed
/// from.</param>
/// <param name="IconCount">The number of icon names <see cref="Icons.IconPackDefinition.Icons"/> just
/// stored (both contract and out-of-contract names counted — mirrors
/// <see cref="Icons.IconPackValidationResult.Valid.IgnoredNames"/>'s own "still valid, still stored"
/// posture).</param>
/// <param name="ImportedFrom">The provenance stamp written — always <paramref name="Slug"/> itself
/// (SPEC F130.5): a pack has no authored-in-place path, so this is never anything else.</param>
public sealed record IconPackInstallResponse(string Slug, int IconCount, string ImportedFrom);
