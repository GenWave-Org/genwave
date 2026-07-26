namespace GenWave.Host.Catalog;

/// <summary>
/// A validated pointer to one file inside a persona catalog entry — its card or its meta document:
/// a relative, traversal-free path under <c>entries/</c> that resolves under the index's own
/// directory (SPEC F90.2), plus the sha256 <see cref="CatalogProxyService"/> verifies the fetched
/// bytes against before they are ever cached or served (SPEC F90.3). Constructing one of these
/// already implies both checks passed — <see cref="CatalogIndexValidator"/> is the only place they
/// are built.
/// </summary>
public sealed record CatalogFileRef(string Path, string Sha256);
