namespace GenWave.Host.Api;

/// <summary>
/// Filter criteria for the bulk eligibility update — mirrors the GET /api/media query params.
///
/// <see cref="ArtistExact"/>/<see cref="AlbumExact"/>/<see cref="GenresExact"/> (SPEC F52.4) are
/// additive case-insensitive EQUALITY filters alongside <see cref="Artist"/>/<see cref="Genre"/>'s
/// substring semantics — mapped straight into <c>MediaQuery</c>'s shared WHERE builder so this
/// endpoint's affected set structurally agrees with an equivalent browse.
///
/// <see cref="MediaIds"/> (SPEC F153.10, STORY-376 AC6, PLAN T378) is the Gardener page's own
/// "Keep this one" bulk action: an explicit id set, ANDed with every other named field on this
/// record rather than routed through the shared, NuGet-published <c>MediaQuery</c> — a Host-only
/// DTO addition so the id predicate never has to become part of that published contract. The
/// controller caps the list at 500 entries (400 <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/>,
/// never echoing the caller's own ids, past that) before this record ever reaches the repository;
/// <see langword="null"/> or empty applies no id constraint, exactly like every other field here.
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
    IReadOnlyList<string>? GenresExact = null,
    IReadOnlyList<long>? MediaIds = null);
