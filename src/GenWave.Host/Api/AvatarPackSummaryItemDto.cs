namespace GenWave.Host.Api;

/// <summary>
/// One item on an <see cref="AvatarPackSummaryDto"/> row (SPEC F128.1, STORY-332, PLAN T294) — a
/// display name and its OPTIONAL "pairs well with" catalog persona slug, NO bytes (mirrors
/// <see cref="CatalogAvatarItemDto"/>'s own two matching fields, minus <c>File</c>: the Wardrobe's
/// Avatars tab reads a face's bytes through the TRANSIENT proxied catalog route instead, the F104
/// specimen precedent, never this durable listing — see <see cref="AvatarPackController.List"/>'s own
/// remarks).
/// </summary>
/// <param name="Name">The item's own name (<c>station.avatar_pack_item.name</c>) — already bounded
/// and shape-gated at install time (<c>AvatarPackController.IsValidItemName</c>).</param>
/// <param name="SuggestedPersona">An OFFER, never an auto-write (SPEC F128.5) — <see langword="null"/>
/// when the pack's manifest named none.</param>
public sealed record AvatarPackSummaryItemDto(string Name, string? SuggestedPersona);
