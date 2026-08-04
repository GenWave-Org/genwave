namespace GenWave.Host.Catalog;

/// <summary>
/// Ephemeral outcome of one catalog entry's card+meta fetch attempt (SPEC F90.3) —
/// <see cref="CatalogProxyService"/> collapses this into a public <see cref="CatalogEntryFetchResult"/>
/// right after (caching on success, logging the required WARN on a withheld failure). Closed
/// hierarchy, same shape as the other outcome types in this folder.
/// </summary>
internal abstract record EntryFetchOutcome
{
    private EntryFetchOutcome() { }

    /// <summary>The WHOLE entry — both manifest and meta fetched and hash-verified.</summary>
    public sealed record Ok(string ManifestJson, string MetaJson) : EntryFetchOutcome;

    /// <summary>
    /// ONE file (card or meta) fetched and hash-verified — an internal building block only
    /// <see cref="CatalogProxyService.FetchAndVerifyEntryAsync"/> ever sees, pairing two of these
    /// into <see cref="Ok"/>. Never returned to a caller outside that one method (review finding:
    /// folded a formerly separate per-file result type into this hierarchy, since the two were
    /// identical apart from needing this one extra case).
    /// </summary>
    public sealed record FileOk(CatalogEntryFilePart Part, string Text) : EntryFetchOutcome;

    public sealed record HashMismatch(CatalogEntryFilePart Part, string Expected, string Actual) : EntryFetchOutcome;
    public sealed record Oversize(CatalogEntryFilePart Part) : EntryFetchOutcome;
    public sealed record NetworkFailure : EntryFetchOutcome;
}
