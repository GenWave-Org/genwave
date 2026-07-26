namespace GenWave.Host.Catalog;

/// <summary>
/// Outcome of one bounded HTTP GET (SPEC F90.3) — shared by <see cref="CatalogProxyService"/>'s two
/// upstream calls (the index, and one card/meta file). Hash verification and index-shape validation
/// are NOT this type's concern (they need context — the expected sha256, the index directory — that
/// a plain fetch doesn't have); this only ever answers "did the bytes arrive, and were they small
/// enough". Closed hierarchy, same shape as <see cref="CatalogIndexFetchResult"/>.
/// </summary>
internal abstract record CatalogFetchOutcome
{
    private CatalogFetchOutcome() { }

    public sealed record Ok(byte[] Bytes) : CatalogFetchOutcome;

    /// <summary>The response body exceeded the caller's cap — withheld before the read even completed.</summary>
    public sealed record Oversize : CatalogFetchOutcome;

    /// <summary>Non-2xx status (including a 3xx redirect — never followed), connect failure, or timeout. <see cref="Detail"/> is the exception message, for the one WARN the caller logs.</summary>
    public sealed record NetworkFailure(string Detail) : CatalogFetchOutcome;
}
