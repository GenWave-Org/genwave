namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="FileActionOutcomeKind"/> (SPEC F154.7; STORY-379; PLAN T380,
/// gh-#529) — the lower-case strings <c>library.file_action.outcome</c> stores (a plain <c>text</c>
/// column, not a Postgres enum — unlike <see cref="FileActionVerbTokens"/>'s own
/// <c>library.file_verb</c>). Mirrors <see cref="RotStateTokens"/>'s own idiom (T380 review N6): the
/// ONE map <c>Garden.FileActions.FileActionExecutor</c> writes through, so a future reader (T381) has
/// a single, tested source for the same strings rather than re-typing them.
/// </summary>
public static class FileActionOutcomeTokens
{
    /// <summary>The wire token for <paramref name="kind"/> — also what
    /// <c>Garden.FileActions.FileActionExecutor</c> writes into <c>library.file_action.outcome</c>.
    /// </summary>
    public static string ToToken(FileActionOutcomeKind kind) => kind switch
    {
        FileActionOutcomeKind.Done => "done",
        FileActionOutcomeKind.Conflict => "conflict",
        FileActionOutcomeKind.Refused => "refused",
        FileActionOutcomeKind.Reverted => "reverted",
        FileActionOutcomeKind.Failed => "failed",
        FileActionOutcomeKind.Busy => "busy",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown file action outcome"),
    };

    /// <summary>Parses a wire token — exact-match, case-sensitive (a machine token off a fixed enum,
    /// never operator free text).</summary>
    public static bool TryParse(string raw, out FileActionOutcomeKind kind)
    {
        switch (raw)
        {
            case "done": kind = FileActionOutcomeKind.Done; return true;
            case "conflict": kind = FileActionOutcomeKind.Conflict; return true;
            case "refused": kind = FileActionOutcomeKind.Refused; return true;
            case "reverted": kind = FileActionOutcomeKind.Reverted; return true;
            case "failed": kind = FileActionOutcomeKind.Failed; return true;
            case "busy": kind = FileActionOutcomeKind.Busy; return true;
            default: kind = default; return false;
        }
    }
}
