namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// Locates and loads the shipped <c>CONTRIBUTING.md</c> for STORY-293's structural and
/// suite↔doc-parity facts. The file lives at the repo root, one level above <c>tests/</c> —
/// <see cref="SolutionLocator.Root"/> finds it without assuming a path depth of its own.
/// </summary>
internal static class ContributingDocument
{
    public static string Read() => File.ReadAllText(Path.Combine(SolutionLocator.Root(), "CONTRIBUTING.md"));

    /// <summary>The zero-based character offset of the first line that is a markdown heading of
    /// level 2 or deeper (<c>"## "</c>, <c>"### "</c>, ...) containing <paramref name="headingSubstring"/>,
    /// or -1 if no such heading exists. Requiring the line to actually BE a heading — <c>#</c>
    /// characters, level ≥ 2, then a space, checked positionally rather than by any looser "starts
    /// with #" test — is what makes "the table's heading offset precedes the first workflow heading"
    /// a structural fact rather than a substring race a stray prose sentence (or the document's own
    /// level-1 title) could win. STORY-293 AC1's own ask ("assert structurally ... not vacuous").
    /// Matched on a plain substring rather than the heading's exact emoji glyph so this stays stable
    /// if the emoji ever changes without the wording changing.</summary>
    public static int IndexOfHeadingContaining(string markdown, string headingSubstring)
    {
        var offset = 0;
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (IsHeadingLevelTwoOrDeeper(trimmed) && trimmed.Contains(headingSubstring, StringComparison.Ordinal))
                return offset;

            offset += line.Length + 1; // +1 for the '\n' Split consumed.
        }

        return -1;
    }

    /// <summary>True when <paramref name="line"/> is one or more <c>#</c> characters (at least two —
    /// level 1 is the document title, never a section heading a workflow detail could follow)
    /// immediately followed by a space, e.g. <c>"## Title"</c> or <c>"### Title"</c> but not
    /// <c>"#Title"</c> (no space: not a heading at all in CommonMark) or a line that merely contains a
    /// <c>#</c> character somewhere.</summary>
    private static bool IsHeadingLevelTwoOrDeeper(string line)
    {
        var hashCount = 0;
        while (hashCount < line.Length && line[hashCount] == '#')
            hashCount++;

        return hashCount >= 2 && hashCount < line.Length && line[hashCount] == ' ';
    }
}
