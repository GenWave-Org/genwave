namespace GenWave.Host.Api;

/// <summary>
/// Filter criteria for the bulk rating endpoints (SPEC F61.1, STORY-158) — mirrors the GET
/// /api/media query parameters and <see cref="BulkReassignFilter"/>'s field set exactly.
///
/// Deliberately excludes <c>neverPlay</c>: <c>MediaQuery.NeverPlay</c> is browse-only (SPEC
/// F33.10) — <c>MediaRepository.BuildAdminWhere</c> never reads it (it needs a JOIN to
/// <c>library.media_rating</c> that the bulk write paths don't have), so a bulk filter that
/// carried it would silently diverge from what the browse preview shows. Structurally omitting
/// the field here — rather than accepting it and ignoring it — makes that divergence impossible
/// to reach through this DTO. <c>eligible</c> is likewise omitted: a rating sweep selects rows by
/// metadata, not by eligibility.
///
/// <see cref="ArtistExact"/>/<see cref="AlbumExact"/>/<see cref="GenresExact"/> (SPEC F52.4) are
/// additive case-insensitive EQUALITY filters alongside <see cref="Artist"/>/<see cref="Genre"/>'s
/// substring semantics — mapped straight into <c>MediaQuery</c>'s shared WHERE builder so a bulk
/// vote/never-play sweep affects exactly the rows the operator previewed on browse (F61.1's "one
/// shared WHERE builder").
/// </summary>
public sealed record BulkRatingFilter(
    string? State,
    string? Artist,
    string? Genre,
    long? LibraryId,
    string? Q,
    string? ArtistExact = null,
    string? AlbumExact = null,
    IReadOnlyList<string>? GenresExact = null);
