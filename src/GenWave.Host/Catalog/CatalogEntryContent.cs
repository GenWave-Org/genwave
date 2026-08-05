namespace GenWave.Host.Catalog;

/// <summary>
/// One catalog entry's fully fetched, hash-verified content (SPEC F90.3) — what
/// <see cref="CatalogProxyService.GetEntryAsync"/> returns on success. Deliberately raw JSON text,
/// not a parsed <c>PersonaCard</c>/<c>ThemeManifest</c>: T101's proxy endpoint serves these bytes
/// back verbatim (the same shape a hand-uploaded manifest/meta pair has), and each kind's own
/// import endpoint (SPEC F90.5, F103.6) is what actually deserializes and validates a manifest's
/// shape at import time — this type never duplicates that parsing.
/// </summary>
public sealed record CatalogEntryContent(
    string Slug,
    CatalogEntryKind Kind,
    CatalogAudience Audience,
    IReadOnlyList<string> BestFor,
    string ManifestJson,
    string MetaJson);
