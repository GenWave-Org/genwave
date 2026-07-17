namespace GenWave.Host.Api;

/// <summary>
/// Filter criteria for the bulk eligibility update — mirrors the GET /api/media query params.
///
/// <see cref="ArtistExact"/>/<see cref="AlbumExact"/>/<see cref="GenresExact"/> (SPEC F52.4) are
/// additive case-insensitive EQUALITY filters alongside <see cref="Artist"/>/<see cref="Genre"/>'s
/// substring semantics — mapped straight into <c>MediaQuery</c>'s shared WHERE builder so this
/// endpoint's affected set structurally agrees with an equivalent browse.
/// </summary>
public sealed record BulkEligibilityFilter(
    string? State,
    string? Artist,
    string? Genre,
    long? LibraryId,
    string? Q,
    bool? Eligible,
    string? ArtistExact = null,
    string? AlbumExact = null,
    IReadOnlyList<string>? GenresExact = null);
