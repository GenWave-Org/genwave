namespace GenWave.Host.Api;

/// <summary>
/// 200 response body for <c>GET /api/catalog/entries/{slug}</c> (SPEC F90.2, F90.3) once a real
/// entry is being served. <see cref="Card"/>/<see cref="Meta"/> are the RAW hash-verified JSON text
/// — the same <c>&lt;slug&gt;.persona.json</c>/<c>&lt;slug&gt;.meta.json</c> bytes a hand-downloaded
/// copy would carry, deliberately never re-parsed/re-projected here (SPEC F90.5: the review-then-
/// import flow deserializes the card itself at import time through the EXISTING F79 import endpoint
/// — this endpoint never duplicates that parsing). See <see cref="CatalogController"/>'s own remarks
/// for the shared <see cref="Unreachable"/>-flag shape this reuses from <see cref="CatalogIndexResponse"/>.
/// </summary>
/// <param name="Card">The persona card's raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Meta">The shelf metadata's raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="FetchedAt">When THIS content was originally fetched (SPEC F90.4's stale-serve stamp); <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Unreachable">
/// <see langword="true"/> when the catalog itself is currently unreachable (no usable index to
/// resolve this slug against) — distinct from a genuinely unknown slug, which 404s instead.
/// </param>
public sealed record CatalogEntryResponse(string? Card, string? Meta, DateTimeOffset? FetchedAt, bool Unreachable);
