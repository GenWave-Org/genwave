namespace GenWave.Host.Catalog;

/// <summary>
/// Every outcome of <see cref="CatalogProxyService.GetAssetAsync"/> (SPEC F104.1, F104.4, T194).
/// Closed hierarchy, the same shape as <see cref="CatalogEntryFetchResult"/> — one binary asset
/// (a font pack's woff2 face or its OFL licence text) instead of a manifest/meta pair.
/// </summary>
public abstract record CatalogAssetFetchResult
{
    private CatalogAssetFetchResult() { }

    /// <summary>
    /// Fresh or stale-but-last-known-good bytes (SPEC F90.4's stale-serve rule, applied to assets) —
    /// see <see cref="CatalogEntryFetchResult.Ok"/>'s own remarks on <see cref="FetchedAt"/>.
    /// </summary>
    public sealed record Ok(byte[] Bytes, DateTimeOffset FetchedAt) : CatalogAssetFetchResult;

    /// <summary>The index is reachable and valid, but names no asset with the requested file under this slug (or no entry with this slug at all).</summary>
    public sealed record NotFound : CatalogAssetFetchResult;

    /// <summary>No usable content, fresh or cached — same "unreachable" semantics as <see cref="CatalogEntryFetchResult.Unreachable"/>.</summary>
    public sealed record Unreachable : CatalogAssetFetchResult;

    /// <summary>
    /// The fetched bytes for <see cref="File"/> do not hash to the index's own <c>sha256</c> (SPEC
    /// F104.1) — withheld, never served, never cached. <see cref="Api.CatalogController"/> maps this
    /// to 502 with a WARN naming <see cref="Slug"/>/<see cref="File"/>/expected/actual already logged
    /// by <see cref="CatalogProxyService"/>.
    /// </summary>
    public sealed record HashMismatch(string Slug, string File) : CatalogAssetFetchResult;

    /// <summary>The fetched bytes for <see cref="File"/> exceeded <see cref="CatalogProxyService.MaxAssetBytes"/> (or the asset's own smaller declared size) — withheld before the read even completes; never served, never cached.</summary>
    public sealed record Oversize(string Slug, string File) : CatalogAssetFetchResult;
}
