namespace GenWave.Host.Catalog;

/// <summary>
/// Every outcome of <see cref="CatalogProxyService.GetIndexAsync"/> (SPEC F90.2, F90.4). Closed
/// hierarchy (private base constructor) so a consumer switches exhaustively — mirrors
/// <c>GenWave.Core.Domain.PersonaImportOutcome</c>'s own shape.
/// </summary>
public abstract record CatalogIndexFetchResult
{
    private CatalogIndexFetchResult() { }

    /// <summary>
    /// A usable shelf listing — either freshly fetched and validated, or the last-known-good cached
    /// copy served under F90.4's stale-on-failure rule. <see cref="FetchedAt"/> is always the moment
    /// THAT listing was originally fetched, never "now" — a stale serve keeps its original stamp so
    /// a consumer can tell how old it is.
    /// </summary>
    public sealed record Ok(IReadOnlyList<CatalogEntrySummary> Entries, DateTimeOffset FetchedAt) : CatalogIndexFetchResult;

    /// <summary>
    /// No usable listing exists: the catalog surface is disabled (<c>Community:CatalogIndexUrl</c>
    /// empty, F90.1), or the origin failed/returned a wholesale-rejected index (F90.2) with no cache
    /// to fall back on (F90.4 — a cold cache beats nothing). T101 maps this to the graceful "catalog
    /// unreachable" empty state, never an error page.
    /// </summary>
    public sealed record Unreachable : CatalogIndexFetchResult;
}
