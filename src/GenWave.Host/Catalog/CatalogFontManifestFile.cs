namespace GenWave.Host.Catalog;

/// <summary>
/// One face inside a <see cref="CatalogFontManifest"/>'s <c>files[]</c> (SPEC F104.1/F104.2) — a
/// font pack is one family, role-agnostic: an upright face and, optionally, a second italic face,
/// never a full weight/style matrix. <see cref="File"/> is the bare filename (FONTS.md's own
/// provenance-record convention, e.g. <c>space-grotesk-variable-latin.woff2</c>) the pack's sibling
/// <see cref="CatalogAssetRef"/> carries under the SAME <c>entries/&lt;slug&gt;/</c> directory —
/// this record names WHICH asset plays which role; it does not itself carry a path or hash (that
/// belongs to the index's own <c>assets[]</c> entry, verified independently). <see cref="Bytes"/>
/// mirrors FONTS.md's own provenance field (measured after subsetting) — the SAME real number the
/// index's paired asset also declares, kept here too since the manifest is the pack's own
/// self-contained provenance record, not just a file index.
/// </summary>
public sealed record CatalogFontManifestFile(string Role, string File, string Weight, string Style, int Bytes);
