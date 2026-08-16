namespace GenWave.Host.Catalog;

/// <summary>
/// One catalog entry's fully fetched, hash-verified content (SPEC F90.3) — what
/// <see cref="CatalogProxyService.GetEntryAsync"/> returns on success. Deliberately raw JSON text,
/// not a parsed <c>PersonaCard</c>/<c>ThemeManifest</c>: T101's proxy endpoint serves these bytes
/// back verbatim (the same shape a hand-uploaded manifest/meta pair has), and each kind's own
/// import endpoint (SPEC F90.5, F103.6) is what actually deserializes and validates a manifest's
/// shape at import time — this type never duplicates that parsing.
///
/// <see cref="Assets"/> (SPEC F104.1, F128.1/.2, T194/T292) is carried straight off the index's own
/// <see cref="CatalogEntrySummary.Assets"/> — see that property's own remarks for which kinds carry
/// what — so <see cref="Api.CatalogController"/>'s font/avatar-kind meta projections (byte total, the
/// specimen's resolved file name, item names) and its persona-kind sidecar-face projection can all
/// read it without a second index lookup; the asset BYTES themselves still only ever reach this
/// process through <see cref="CatalogProxyService.GetAssetAsync"/>'s own separate, size-capped,
/// hash-verified fetch — never through this type.
/// </summary>
public sealed record CatalogEntryContent(
    string Slug,
    CatalogEntryKind Kind,
    CatalogAudience Audience,
    IReadOnlyList<string> BestFor,
    string ManifestJson,
    string MetaJson,
    IReadOnlyList<CatalogAssetRef> Assets);
