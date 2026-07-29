using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Admin-specific paged catalog query returning the richer <see cref="AdminMediaDto"/>
/// projection (T048). Kept separate from <see cref="IMediaCatalog"/> so the playout
/// path is not touched by admin schema evolution, and so test fakes for the playout
/// path do not need to know about admin fields.
/// </summary>
public interface IAdminMediaQuery
{
    /// <summary>
    /// Paged, filtered list of admin media rows scoped to the given libraries (T048).
    /// An empty scope short-circuits to an empty result without touching the database
    /// (default-deny). Returns <see cref="AdminMediaDto"/> with state, format, and all
    /// enrichment columns so the admin UI receives a single flat JSON object per row.
    ///
    /// Every row's <c>Score</c>/<c>NeverPlay</c> resolve via a LEFT JOIN + COALESCE against
    /// <c>library.media_rating</c> — an unrated row reads the F33.2 ledger default (SPEC F33.10).
    /// <see cref="MediaQuery.NeverPlay"/> <c>true</c> narrows to flagged rows only; absent/false
    /// applies no filter (see that field's doc for the rationale).
    ///
    /// When <see cref="MediaQuery.HidesUnavailable"/> is true (gh-#113 — no explicit state filter,
    /// no <c>IncludeUnavailable</c> opt-in), <c>unavailable</c> rows are excluded from the page and
    /// the total. Browse-only, like <c>NeverPlay</c>: the bulk write paths sharing the same
    /// WHERE-builder still reach unavailable rows — a deliberate asymmetry, since hiding is a view
    /// default rather than a filter the operator chose, and an unavailable row must stay reachable
    /// by every curation write exactly as before.
    /// </summary>
    Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct);

    /// <summary>
    /// The number of <c>unavailable</c> rows that <paramref name="query"/>'s OTHER filters (scope,
    /// search, facets, rating flag — everything except a state filter) match (gh-#113) — the "N
    /// unavailable tracks hidden" figure the catalog browse surfaces next to a page that
    /// <see cref="MediaQuery.HidesUnavailable"/> excluded them from. An empty scope
    /// short-circuits to 0 without touching the database (default-deny).
    ///
    /// Default-implemented (returns 0) so existing read-only test doubles keep compiling; the
    /// concrete repository overrides it with the real count.
    /// </summary>
    Task<int> CountUnavailableAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        Task.FromResult(0);
}
