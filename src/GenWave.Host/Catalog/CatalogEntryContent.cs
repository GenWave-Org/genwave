namespace GenWave.Host.Catalog;

/// <summary>
/// One catalog entry's fully fetched, hash-verified content (SPEC F90.3) — what
/// <see cref="CatalogProxyService.GetEntryAsync"/> returns on success. Deliberately raw JSON text,
/// not a parsed <c>PersonaCard</c>: T101's proxy endpoint serves these bytes back verbatim (the
/// same shape a hand-uploaded card/meta pair has), and the EXISTING F79 import endpoint (SPEC
/// F90.5) is what actually deserializes and validates a card's shape at import time — this type
/// never duplicates that parsing.
/// </summary>
public sealed record CatalogEntryContent(
    string Slug,
    CatalogAudience Audience,
    IReadOnlyList<string> BestFor,
    string CardJson,
    string MetaJson);
