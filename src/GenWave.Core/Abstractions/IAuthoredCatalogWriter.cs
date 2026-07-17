using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Authored-insert catalog seam (F27.1, F27.2, F27.8): lands a generated safe-segment artifact as a
/// normal <c>library.media</c> row, directly in <c>state='ready'</c>, with no enricher round-trip.
/// Kept separate from <see cref="IMediaCatalog"/> (scoped reads) and <see cref="IAdminMediaWrite"/>
/// (operator patch of an existing row) because its caller is a generation pipeline creating a brand
/// new row, not a request handler mutating one that already exists.
/// </summary>
public interface IAuthoredCatalogWriter
{
    /// <summary>
    /// Inserts <paramref name="insert"/> as a single INSERT ... RETURNING id — one round-trip, so a
    /// rejected insert (see below) writes nothing.
    /// </summary>
    /// <returns>The id of the newly inserted row.</returns>
    /// <remarks>
    /// When <see cref="AuthoredMediaInsert.LibraryId"/> references no row in <c>library.library</c>,
    /// the underlying foreign-key violation (Postgres SQLSTATE 23503) propagates to the caller
    /// unmapped — the insert never committed, so nothing is written (F27.1 sad path).
    /// </remarks>
    Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct);
}
