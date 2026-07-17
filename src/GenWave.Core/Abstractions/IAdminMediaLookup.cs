using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Unscoped media lookup for object-level authorization in admin endpoints (T042).
/// Intentionally separate from <see cref="IMediaCatalog"/> so that <see cref="IMediaCatalog"/>
/// can enforce the invariant that every method requires a <see cref="LibraryScope"/> parameter.
/// This seam is only resolved by admin controller code; nothing in the playout path touches it.
/// </summary>
public interface IAdminMediaLookup
{
    /// <summary>
    /// Returns the admin media DTO plus its <c>library_id</c> without scope filtering so the
    /// caller can perform an object-level authorization check (IDOR-safe 403 vs 404 decision).
    /// Returns the full <see cref="AdminMediaDto"/> (state, format, all enrichment columns) so
    /// the controller can serve the 200 response without a second query (T048).
    /// Returns null when no row exists with the given id.
    /// Never used on a public endpoint; the caller is responsible for the 403 / 404 decision.
    /// </summary>
    Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct);
}
