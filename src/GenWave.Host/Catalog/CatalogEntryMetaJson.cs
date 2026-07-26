namespace GenWave.Host.Catalog;

/// <summary>
/// Ephemeral JSON projection of a fetched, hash-verified <c>&lt;slug&gt;.meta.json</c> document
/// (SPEC F90.3, F90.4a) — parsed by <see cref="Api.CatalogController"/> solely to surface the
/// shelf's detail-panel display fields (author, description, sample patter) on
/// <c>GET /api/catalog/entries/{slug}</c>. <c>audience</c>/<c>bestFor</c> are deliberately never
/// re-read from here: <see cref="CatalogEntryContent"/> already carries those, sourced from the
/// hash-verified index.json entry itself (T101), not re-derived from this file a second time.
///
/// <para>
/// All-nullable, mirroring <see cref="CatalogIndexValidator"/>'s own ephemeral JSON records: this
/// content IS trusted (genwave-catalog's CI schema-validates every meta.json before it is ever
/// published, F89.2, and this station verifies its sha256 before this type ever sees the bytes,
/// F90.3) — but a parse failure still degrades to "nothing to show" for these three optional
/// display fields rather than a 500, the same tolerant posture every other JSON projection in this
/// codebase takes for content it doesn't fully control the origin of.
/// </para>
/// </summary>
public sealed record CatalogEntryMetaJson
{
    public string? Author { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? SamplePatter { get; init; }
}
