namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="FileActionVerb"/> (SPEC F154.1, F154.7; STORY-379; PLAN T380,
/// gh-#529) — the snake_case strings <c>library.file_verb</c> itself uses in Postgres
/// (<c>db/41-gardener-migration.sh</c>: <c>'retag'</c>, <c>'rename'</c>, <c>'move'</c>). Mirrors
/// <see cref="RotKindTokens"/>'s own idiom — the ONE map <c>Garden.FileActions.FileActionRepository</c>
/// binds the <c>::library.file_verb</c> SQL parameter through and parses a read-back row's
/// <c>verb::text</c> with.
/// </summary>
public static class FileActionVerbTokens
{
    /// <summary>The wire token for <paramref name="verb"/> — also what
    /// <c>Garden.FileActions.FileActionRepository</c> binds as the <c>::library.file_verb</c> SQL
    /// parameter.</summary>
    public static string ToToken(FileActionVerb verb) => verb switch
    {
        FileActionVerb.Retag => "retag",
        FileActionVerb.Rename => "rename",
        FileActionVerb.Move => "move",
        _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, "unknown file action verb"),
    };

    /// <summary>Parses a wire token — exact-match, case-sensitive (a machine token off a fixed enum,
    /// never operator free text).</summary>
    public static bool TryParse(string raw, out FileActionVerb verb)
    {
        switch (raw)
        {
            case "retag": verb = FileActionVerb.Retag; return true;
            case "rename": verb = FileActionVerb.Rename; return true;
            case "move": verb = FileActionVerb.Move; return true;
            default: verb = default; return false;
        }
    }
}
