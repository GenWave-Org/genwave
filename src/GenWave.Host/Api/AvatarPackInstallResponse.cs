namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/avatar-packs/{slug}/install</c> (SPEC F128.3, STORY-332, PLAN T293) —
/// the avatar-kind sibling of <see cref="FontPackInstallResponse"/>, same "only what the accepted
/// write genuinely hands back" posture. Narrower than a full library row
/// (<c>GenWave.Core.Domain.AvatarPack</c>): the Wardrobe Avatars-tab listing route (PLAN T294) is the
/// future full-detail read.
/// </summary>
/// <param name="Slug">The route slug — the upsert key, and the catalog entry this pack installed
/// from.</param>
/// <param name="PackName">The manifest's own display pack name.</param>
/// <param name="Items">The item names just installed, in manifest order.</param>
/// <param name="ImportedFrom">The provenance stamp written — always <paramref name="Slug"/> itself
/// (SPEC F128.3): a pack has no authored-in-place or file-upload path, so this is never anything
/// else.</param>
public sealed record AvatarPackInstallResponse(string Slug, string PackName, IReadOnlyList<string> Items, string ImportedFrom);
