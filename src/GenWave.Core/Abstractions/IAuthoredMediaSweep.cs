using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Library-side half of the retired ad audio sweep (gh-#3): lists and deletes ineligible authored rows
/// in one library. Its own seam, kept off <see cref="IAuthoredCatalogWriter"/> so the delete path costs
/// that interface's many test doubles nothing. Only ever called for the ads library — safe segments and
/// jingle packs are never auto-deleted (an operator may have switched <c>eligible</c> off on purpose).
/// </summary>
public interface IAuthoredMediaSweep
{
    /// <summary>Rows in <paramref name="libraryId"/> with <c>eligible = false</c> whose file was last
    /// written before <paramref name="modifiedBefore"/>. Bounded; a backlog drains over several sweeps.</summary>
    Task<IReadOnlyList<AuthoredMediaFile>> ListIneligibleAsync(
        long libraryId, DateTimeOffset modifiedBefore, CancellationToken ct);

    /// <summary>Deletes <paramref name="mediaId"/>'s row, but only while it is still ineligible.
    /// <see langword="false"/> when no such row exists or it was made eligible meanwhile.</summary>
    Task<bool> DeleteIneligibleAsync(long mediaId, CancellationToken ct);
}
