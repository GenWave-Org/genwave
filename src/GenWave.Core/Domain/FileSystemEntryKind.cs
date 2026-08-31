namespace GenWave.Core.Domain;

/// <summary>
/// What a path names on disk, as answered by <see cref="Abstractions.IFileSystemProbe.Kind"/> (SPEC
/// F154.3, F154.4; STORY-379; PLAN T379, gh-#529 — T379 review N9b). One probe, one answer — a
/// caller that needs "does it exist" tests <c>!= Missing</c>; a caller that needs "is it specifically
/// a directory" (the Library Gardener's move-target check) tests <c>== Directory</c> directly, rather
/// than composing two separately-timed booleans that could disagree.
/// </summary>
public enum FileSystemEntryKind
{
    /// <summary>Nothing exists at the path.</summary>
    Missing,

    /// <summary>The path names an ordinary file.</summary>
    File,

    /// <summary>The path names a directory.</summary>
    Directory,
}
