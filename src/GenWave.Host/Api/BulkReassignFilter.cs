namespace GenWave.Host.Api;

/// <summary>
/// Filter criteria for the bulk library reassignment endpoint (L4 / STORY-049) —
/// mirrors the GET /api/media query parameters.
///
/// The <c>eligible</c> field is intentionally omitted: reassignment selects SOURCE rows by
/// metadata (state, artist, genre, search text, library). All fields are optional; absent
/// fields add no predicate beyond the station scope.
///
/// <see cref="ArtistExact"/>/<see cref="AlbumExact"/>/<see cref="GenresExact"/> (SPEC F52.4) are
/// additive case-insensitive EQUALITY filters alongside <see cref="Artist"/>/<see cref="Genre"/>'s
/// substring semantics — mapped straight into <c>MediaQuery</c>'s shared WHERE builder so this
/// endpoint's affected set structurally agrees with an equivalent browse.
/// </summary>
public sealed record BulkReassignFilter(
    string? State,
    string? Artist,
    string? Genre,
    long? LibraryId,
    string? Q,
    string? ArtistExact = null,
    string? AlbumExact = null,
    IReadOnlyList<string>? GenresExact = null);
