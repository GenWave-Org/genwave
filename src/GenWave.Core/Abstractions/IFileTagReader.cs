using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Reads a file's OWN tags off disk (SPEC F154.1; STORY-379; PLAN T381 review N4, gh-#529) — the
/// retag dry-run's own source for the file's current tags, read by <see cref="IFileActionPlanner"/>
/// itself (see that interface's own remarks) ONLY AFTER a subject has already passed the jail's own
/// destination gate — this port is the ONE place a file's current tags are actually opened, so the
/// planner has a single, testable seam rather than reaching for TagLibSharp directly. Implemented in
/// <c>GenWave.MediaLibrary</c> (<c>Garden.FileActions.FileTagReader</c>).
/// </summary>
public interface IFileTagReader
{
    /// <summary>
    /// Reads <paramref name="path"/>'s current tags, or <see langword="null"/> when the read fails
    /// for any reason (missing file, permission denied, or a corrupt/unrecognised container) — a
    /// failure reads exactly like a tagless file (the planner's own <c>TagDiffCalculator</c> already
    /// treats a <see langword="null"/> <see cref="FileTags"/> that way), never an exception the
    /// caller must catch.
    /// </summary>
    FileTags? TryRead(string path);
}
