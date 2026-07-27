namespace GenWave.Host.Api;

/// <summary>
/// 200 response body for <c>GET /api/catalog/entries/{slug}</c> (SPEC F90.2, F90.3, F90.4a) once a
/// real entry is being served. <see cref="Card"/>/<see cref="Meta"/> stay the RAW hash-verified
/// JSON text — the same <c>&lt;slug&gt;.persona.json</c>/<c>&lt;slug&gt;.meta.json</c> bytes a
/// hand-downloaded copy would carry (SPEC F90.5: the review-then-import flow deserializes the card
/// itself at import time through the EXISTING F79 import endpoint — this endpoint never duplicates
/// that parsing). <see cref="Audience"/>/<see cref="BestFor"/>/<see cref="Author"/>/
/// <see cref="Description"/>/<see cref="SamplePatter"/> are the T102 addition: the Admin UI's
/// shelf detail panel needs these READABLE, not re-parsed client-side out of raw JSON text it was
/// never meant to interpret — so <see cref="CatalogController"/> projects them once, server-side,
/// from the SAME hash-verified <see cref="CatalogEntryContent"/> this whole response is built from
/// (<see cref="Audience"/>/<see cref="BestFor"/> straight off it; <see cref="Author"/>/
/// <see cref="Description"/>/<see cref="SamplePatter"/> parsed out of <see cref="Meta"/> via
/// <see cref="Catalog.CatalogEntryMetaJson"/>). See <see cref="CatalogController"/>'s own remarks
/// for the shared <see cref="Unreachable"/>-flag shape this reuses from <see cref="CatalogIndexResponse"/>.
/// </summary>
/// <param name="Card">The persona card's raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Meta">The shelf metadata's raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="FetchedAt">When THIS content was originally fetched (SPEC F90.4's stale-serve stamp); <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Unreachable">
/// <see langword="true"/> when the catalog itself is currently unreachable (no usable index to
/// resolve this slug against) — distinct from a genuinely unknown slug, which 404s instead.
/// </param>
/// <param name="Audience"><c>"everyone"</c> or <c>"mature"</c> (same lowercase wire vocabulary as <see cref="CatalogShelfEntryDto.Audience"/>), or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="BestFor">Optional genre chips (F90.4a), or <see langword="null"/> when <see cref="Unreachable"/> — empty, never null, once reachable.</param>
/// <param name="Author">The entry's credited author (F90.4a), or <see langword="null"/> when unreachable or absent from meta.json.</param>
/// <param name="Description">The entry's shelf description (F90.4a), or <see langword="null"/> when unreachable or absent from meta.json.</param>
/// <param name="SamplePatter">Sample patter lines (F90.4a), or <see langword="null"/> when <see cref="Unreachable"/> — empty, never null, once reachable.</param>
public sealed record CatalogEntryResponse(
    string? Card,
    string? Meta,
    DateTimeOffset? FetchedAt,
    bool Unreachable,
    string? Audience,
    IReadOnlyList<string>? BestFor,
    string? Author,
    string? Description,
    IReadOnlyList<string>? SamplePatter);
