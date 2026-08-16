namespace GenWave.Host.Catalog;

/// <summary>
/// One face inside a <see cref="CatalogAvatarPackManifest"/>'s <c>items[]</c> (SPEC F128.1) — a pack
/// is a curated set of faces, each with its own display <see cref="Name"/> and an OPTIONAL
/// <see cref="SuggestedPersona"/> hint (a catalog persona slug the item pairs well with — an OFFER,
/// never an auto-write, the same soft-suggestion posture <c>CatalogEntryMetaJson.SuggestedPersona</c>
/// already has for a show entry). <see cref="File"/> is the bare filename (mirrors
/// <see cref="CatalogFontManifestFile.File"/>'s own remarks) the pack's sibling
/// <see cref="CatalogAssetRef"/> carries under the SAME <c>entries/&lt;slug&gt;/</c> directory — this
/// record names WHICH asset the item is; it does not itself carry a path or hash.
/// </summary>
public sealed record CatalogAvatarPackItem(string Name, string File, string? SuggestedPersona);
