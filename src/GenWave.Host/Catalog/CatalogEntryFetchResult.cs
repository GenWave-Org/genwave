namespace GenWave.Host.Catalog;

/// <summary>
/// Every outcome of <see cref="CatalogProxyService.GetEntryAsync"/> (SPEC F90.2-F90.4). Closed
/// hierarchy, the same shape as <see cref="CatalogIndexFetchResult"/>.
/// </summary>
public abstract record CatalogEntryFetchResult
{
    private CatalogEntryFetchResult() { }

    /// <summary>
    /// Fresh or stale-but-last-known-good content (SPEC F90.4) — see
    /// <see cref="CatalogIndexFetchResult.Ok"/>'s own remarks on <see cref="FetchedAt"/>.
    /// </summary>
    public sealed record Ok(CatalogEntryContent Content, DateTimeOffset FetchedAt) : CatalogEntryFetchResult;

    /// <summary>The index is reachable and valid, but names no entry with the requested slug.</summary>
    public sealed record NotFound : CatalogEntryFetchResult;

    /// <summary>
    /// No usable content, fresh or cached — the index itself is unreachable/disabled, or this slug
    /// was never fetched before and the origin just failed. Same "unreachable" semantics as
    /// <see cref="CatalogIndexFetchResult.Unreachable"/>.
    /// </summary>
    public sealed record Unreachable : CatalogEntryFetchResult;

    /// <summary>
    /// The fetched bytes for <see cref="Part"/> do not hash to the index's own <c>sha256</c> (SPEC
    /// F90.3) — withheld, never served, never cached. T101 maps this to 502 with a WARN naming
    /// <see cref="Slug"/>, <see cref="ExpectedSha256"/>, and <see cref="ActualSha256"/>.
    /// </summary>
    public sealed record HashMismatch(string Slug, CatalogEntryFilePart Part, string ExpectedSha256, string ActualSha256) : CatalogEntryFetchResult;

    /// <summary>
    /// The fetched bytes for <see cref="Part"/> exceeded its size cap (SPEC F90.3) — withheld
    /// before the read even completes; never served, never cached.
    /// </summary>
    public sealed record Oversize(string Slug, CatalogEntryFilePart Part) : CatalogEntryFetchResult;
}
