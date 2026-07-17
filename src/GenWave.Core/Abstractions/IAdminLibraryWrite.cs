using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Admin library management write operations (STORY-046, Epic J).
/// Kept separate from read concerns so test doubles for read-only scenarios
/// do not need to implement mutation methods.
/// </summary>
public interface IAdminLibraryWrite
{
    /// <summary>
    /// Creates a new library with the given <paramref name="name"/>.
    /// Returns <see cref="LibraryWriteResult.Created"/> with the new row id on success,
    /// or <see cref="LibraryWriteResult.NameConflict"/> if a library with that name already exists.
    /// </summary>
    Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct);

    /// <summary>
    /// Renames the library identified by <paramref name="id"/> to <paramref name="name"/>.
    /// Returns <see cref="LibraryWriteResult.Renamed"/> on success,
    /// <see cref="LibraryWriteResult.NotFound"/> if no such library exists, or
    /// <see cref="LibraryWriteResult.NameConflict"/> if another library already holds that name.
    /// </summary>
    Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct);

    /// <summary>
    /// Deletes the library identified by <paramref name="id"/>.
    /// Returns <see cref="LibraryWriteResult.Deleted"/> on success,
    /// <see cref="LibraryWriteResult.NotFound"/> if no such library exists, or
    /// <see cref="LibraryWriteResult.HasDependents"/> if media rows still reference it.
    /// </summary>
    Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct);
}
