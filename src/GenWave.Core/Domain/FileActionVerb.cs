namespace GenWave.Core.Domain;

/// <summary>
/// The Library Gardener's three file actions (SPEC F154.1; STORY-379; PLAN T379, gh-#529). No delete
/// verb exists — the gardener writes tags, renames, and moves within the same library root; it never
/// removes a file.
/// </summary>
public enum FileActionVerb
{
    /// <summary>Writes the catalog's artist/title/album/year/genre into the file's own tags via
    /// TagLibSharp; audio bytes are untouched (F154.1). <see cref="Abstractions.IFileActionPlanner"/>
    /// never opens the file itself — the caller supplies the file's current tags on
    /// <see cref="FileActionSubject.CurrentFileTags"/>.</summary>
    Retag,

    /// <summary>Renames the file within its own directory — either the operator-supplied name or the
    /// <c>{artist} - {title}.{ext}</c> template (F154.1).</summary>
    Rename,

    /// <summary>Moves the file to a directory under the SAME library root (F154.1) — never across
    /// roots, never across libraries.</summary>
    Move,
}
