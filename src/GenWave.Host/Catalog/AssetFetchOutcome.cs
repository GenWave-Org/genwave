namespace GenWave.Host.Catalog;

/// <summary>
/// Ephemeral outcome of one binary asset's fetch attempt (SPEC F104.1, T194) —
/// <see cref="CatalogProxyService"/> collapses this into a public <see cref="CatalogAssetFetchResult"/>
/// right after (caching on success, logging the required WARN on a withheld failure). Mirrors
/// <see cref="EntryFetchOutcome"/>'s own shape, minus that type's <c>FileOk</c>/<c>Part</c>-tagging
/// concerns — an asset fetch is always exactly ONE file, never a manifest+meta pair, so there is no
/// "pair two of these together" step here.
/// </summary>
internal abstract record AssetFetchOutcome
{
    private AssetFetchOutcome() { }

    public sealed record Ok(byte[] Bytes) : AssetFetchOutcome;
    public sealed record HashMismatch(string Expected, string Actual) : AssetFetchOutcome;
    public sealed record Oversize : AssetFetchOutcome;
    public sealed record NetworkFailure : AssetFetchOutcome;
}
