using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Reads a file action's subject snapshot for the Library Gardener's dry-run endpoint (SPEC F154.1,
/// F154.3, F154.5; STORY-379; PLAN T381, gh-#529) — the ONE public seam a Host controller needs onto
/// <c>MediaLibrary.Garden.FileActions.FileActionRepository</c>'s own <c>ReadSubjectAsync</c>, mirroring
/// <see cref="IAdminMediaLookup"/>'s own "public port over an otherwise-internal repository" shape
/// (<c>GenWave.MediaLibrary</c>'s L2 boundary keeps every <c>*Repository</c> type internal to its own
/// project; a Host controller reaches it only through a port like this one, never the concrete type).
/// </summary>
public interface IFileActionSubjectReader
{
    /// <summary>
    /// Reads <paramref name="mediaId"/>'s current catalog snapshot — path, xmin, library id, and
    /// catalog tag fields — as a <see cref="FileActionSubject"/> ready for
    /// <see cref="IFileActionPlanner.Plan"/>. This port performs no file I/O of its own (T381 review
    /// N4: a retag's own file tags are read by <see cref="IFileActionPlanner"/> itself, via
    /// <see cref="IFileTagReader"/>, only AFTER the subject has already passed the jail's own
    /// destination gate — never here, and never by this port's own caller).
    /// <see langword="null"/> when no row exists with this id.
    /// </summary>
    Task<FileActionSubject?> ReadSubjectAsync(long mediaId, CancellationToken ct);
}
