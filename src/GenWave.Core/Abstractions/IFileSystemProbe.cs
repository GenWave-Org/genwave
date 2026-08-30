using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The one path <see cref="IFileActionPlanner"/>'s jail reaches the filesystem (SPEC F154.3;
/// STORY-379; PLAN T379, gh-#529) — read-only, so the planner stays pure everywhere else.
/// <see cref="Kind"/> answers what already occupies a candidate path (F154.4's never-overwrite check,
/// and T379 review N9b's "is a move's destination directory actually a file" check — ONE probe
/// answers both, rather than two separately-timed booleans that could disagree).
/// <see cref="ResolveLinks"/> answers what a path's ancestor chain actually resolves to on disk,
/// walking each segment from the filesystem root down so a symlinked DIRECTORY partway through the
/// path — not just the leaf entry — is caught (the mutation pin F154.3 names explicitly). Neither
/// method creates, deletes, or opens a file for content.
/// </summary>
public interface IFileSystemProbe
{
    /// <summary>What already occupies <paramref name="path"/> (F154.4). The jail consults this LAST,
    /// only once every other rule has already passed — never on a path the jail is about to refuse
    /// for another reason.</summary>
    FileSystemEntryKind Kind(string path);

    /// <summary>
    /// Walks <paramref name="path"/>'s ancestor chain from the filesystem root down, resolving each
    /// segment that is itself a symlink to its final target, and returns the fully resolved absolute
    /// path. A path with no symlinked ancestor (the common case) resolves to itself; a path that does
    /// not exist yet resolves to itself too — there is nothing on disk to follow.
    ///
    /// <para>
    /// Returns <see langword="null"/> when the chain cannot be safely resolved at all — a symlink
    /// cycle or a permission failure partway through (T379 review N2) — rather than throwing. The
    /// planner treats <see langword="null"/> as an automatic containment failure (never trust a path
    /// this method could not vouch for), the same fail-closed posture as a resolution that lands
    /// outside the root.
    /// </para>
    /// </summary>
    string? ResolveLinks(string path);
}
