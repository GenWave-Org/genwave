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

    /// <summary>
    /// Resolves a single <c>library.library</c> row by its exact (ordinal, case-sensitive) name, or
    /// <see langword="null"/> when no row carries that name (PLAN T396 review carry-forward F3, an
    /// additive member — the plain-addition posture this interface's own T395 history already
    /// established): the right altitude for a name-keyed lookup —
    /// <see cref="GetAllWithMediaCountAsync"/>-then-scan-in-memory is the wrong one, the exact shape
    /// this member replaces at both of its callers (<c>GenWave.Ads.AdsLibrarySeeder</c>,
    /// <c>GenWave.Ads.LibraryAdSpotSource</c>).
    /// </summary>
    Task<LibraryAdminInfo?> GetByNameAsync(string name, CancellationToken ct);
}
