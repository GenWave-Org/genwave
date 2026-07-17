using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Read access to <c>library.library</c> — resolves display names and media counts.
/// Owned by <c>GenWave.MediaLibrary</c> (same library_svc data source).
/// </summary>
public interface ILibraryRepository
{
    /// <summary>
    /// Returns <see cref="LibraryInfo"/> rows for the given ids.
    /// Ids not found in the database are simply omitted from the result (no error).
    /// </summary>
    Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);

    /// <summary>
    /// Returns every row in <c>library.library</c> with its associated media count
    /// (COUNT of rows in <c>library.media</c> whose <c>library_id</c> matches).
    /// NOT filtered by station scope — returns the global library catalogue.
    /// </summary>
    Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct);
}
