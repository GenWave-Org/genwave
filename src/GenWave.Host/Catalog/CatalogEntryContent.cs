namespace GenWave.Host.Catalog;

/// <summary>
/// One catalog entry's fully fetched, hash-verified content (SPEC F90.3) — what
/// <see cref="CatalogProxyService.GetEntryAsync"/> returns on success. Deliberately raw JSON text,
/// not a parsed <c>PersonaCard</c>/<c>ThemeManifest</c>: T101's proxy endpoint serves these bytes
/// back verbatim (the same shape a hand-uploaded manifest/meta pair has), and each kind's own
/// import endpoint (SPEC F90.5, F103.6) is what actually deserializes and validates a manifest's
/// shape at import time — this type never duplicates that parsing.
///
/// <see cref="Assets"/> (SPEC F104.1, T194) is carried straight off the index's own
/// <see cref="CatalogEntrySummary.Assets"/> — empty for every non-font entry — so
/// <see cref="Api.CatalogController"/>'s font-kind meta projection (byte total, the specimen's
/// resolved file name) can read it without a second index lookup; the asset BYTES themselves still
/// only ever reach this process through <see cref="CatalogProxyService.GetAssetAsync"/>'s own
/// separate, size-capped, hash-verified fetch — never through this type.
/// </summary>
public sealed record CatalogEntryContent(
    string Slug,
    CatalogEntryKind Kind,
    CatalogAudience Audience,
    IReadOnlyList<string> BestFor,
    string ManifestJson,
    string MetaJson,
    IReadOnlyList<CatalogAssetRef> Assets);
