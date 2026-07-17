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
    /// </summary>
    Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct);
}
