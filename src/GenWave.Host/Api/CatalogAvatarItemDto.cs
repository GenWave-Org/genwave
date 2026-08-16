namespace GenWave.Host.Api;

/// <summary>
/// One face on an avatar pack's detail projection (SPEC F128.1, F128.4, PLAN T292) — the wire
/// projection of one <see cref="Catalog.CatalogAvatarPackItem"/>, read off the pack's own fetched,
/// hash-verified <c>.avatar.json</c> manifest (<see cref="CatalogEntryResponse.AvatarItems"/>'s own
/// remarks). <see cref="File"/> is the bare filename <c>GET /api/catalog/entries/{slug}/assets/{file}</c>
/// already serves (the SAME asset-generic route a font pack's specimen face rides) — the Wardrobe's
/// future Avatars tab grid (PLAN T294) passes it straight through, no further lookup.
/// </summary>
/// <param name="Name">The item's display name (SPEC F128.1's <c>items[].name</c>).</param>
/// <param name="File">
/// The bare filename this item's face rides on the pack's own <c>assets[]</c>, or
/// <see langword="null"/> when the manifest names a file the index's own hash-verified
/// <c>assets[]</c> never actually declared (review finding, PLAN T292 —
/// <see cref="CatalogController.ResolveDeclaredAssetFile"/>'s own remarks; mirrors
/// <see cref="CatalogEntryResponse.FontSpecimenFile"/>'s own "never trust a manifest-only filename
/// alone" posture for a font pack).
/// </param>
/// <param name="SuggestedPersona">
/// An OPTIONAL catalog persona slug this face pairs well with (SPEC F128.1's <c>items[].suggestedPersona</c>)
/// — an OFFER, never an auto-write, the same soft-suggestion posture <see cref="CatalogEntryResponse.SuggestedPersona"/>
/// already has for a show entry. Shape-checked the SAME way (a real catalog slug, ≤64 chars —
/// <see cref="CatalogController.ValidateSuggestedPersonaShape"/>) before it ever reaches this DTO;
/// <see langword="null"/> when absent or malformed.
/// </param>
public sealed record CatalogAvatarItemDto(string Name, string? File, string? SuggestedPersona);
