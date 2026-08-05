namespace GenWave.Host.Catalog;

/// <summary>
/// A validated pointer to one binary asset inside a <see cref="CatalogEntryKind.Font"/> pack (SPEC
/// F104.1) — a latin-subsetted woff2 face or the pack's OFL licence text: a relative,
/// traversal-free path under <c>entries/</c> that resolves under the index's own directory (the
/// same SPEC F90.2 belt-and-braces rule <see cref="CatalogFileRef"/> already enforces for a
/// manifest/meta pointer), plus the sha256 the fetch transport verifies the streamed bytes against
/// (T194) and the declared <see cref="Bytes"/> count that same transport size-caps the stream
/// against WHILE downloading — unlike a manifest/meta document (small, JSON, read whole before any
/// check matters), a font asset can be tens of kilobytes, so its size is declared up front rather
/// than only discovered after the fact. Constructing one of these already implies every
/// <see cref="CatalogIndexValidator"/> shape check passed — that class is the only place they are
/// built. <see cref="Bytes"/> is <see langword="long"/> (S2 review finding, T193) — a real byte
/// count is a <see cref="long"/>-shaped quantity house-wide (e.g. <see cref="Stream.Length"/>), and
/// this record only ever carries a value <see cref="CatalogIndexValidator"/> already confirmed is
/// positive and within JSON's own numeric range; see <c>CatalogIndexValidator.CatalogAssetJson</c>'s
/// own remarks on why the ephemeral JSON projection this is built from carries the same width.
/// </summary>
public sealed record CatalogAssetRef(string Path, string Sha256, long Bytes);
