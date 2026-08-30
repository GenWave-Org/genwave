namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="FileActionRule"/> (SPEC F154.3, F154.7; STORY-379; PLAN T380
/// review, small item 1) — the snake_case strings <c>library.file_action.detail</c>'s own
/// <c>rule</c> field carries (<c>Garden.FileActions.FileActionExecutor.DetailForRule</c> uses this
/// instead of the raw <see cref="Enum.ToString()"/> PascalCase spelling, so the audit row's own JSON
/// reads <c>"cross_device"</c>/<c>"leftover_backup"</c>, not <c>"CrossDevice"</c>/
/// <c>"LeftoverBackup"</c>). Mirrors <see cref="FileActionOutcomeTokens"/>'s own idiom exactly — the
/// ONE map <c>FileActionExecutor</c> writes through.
/// </summary>
public static class FileActionRuleTokens
{
    /// <summary>The wire token for <paramref name="rule"/> — also what
    /// <c>Garden.FileActions.FileActionExecutor</c> writes into <c>detail.rule</c>.</summary>
    public static string ToToken(FileActionRule rule) => rule switch
    {
        FileActionRule.NotScannedLibrary => "not_scanned_library",
        FileActionRule.SubjectOutsideRoot => "subject_outside_root",
        FileActionRule.Traversal => "traversal",
        FileActionRule.MissingTarget => "missing_target",
        FileActionRule.InvalidName => "invalid_name",
        FileActionRule.OutsideRoot => "outside_root",
        FileActionRule.SymlinkEscape => "symlink_escape",
        FileActionRule.ExemptRoot => "exempt_root",
        FileActionRule.SameAsSource => "same_as_source",
        FileActionRule.TargetNotADirectory => "target_not_a_directory",
        FileActionRule.TargetExists => "target_exists",
        FileActionRule.NothingToRetag => "nothing_to_retag",
        FileActionRule.SymlinkedTarget => "symlinked_target",
        FileActionRule.CrossDevice => "cross_device",
        FileActionRule.LeftoverBackup => "leftover_backup",
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "unknown file action rule"),
    };

    /// <summary>Parses a wire token — exact-match, case-sensitive (a machine token off a fixed enum,
    /// never operator free text).</summary>
    public static bool TryParse(string raw, out FileActionRule rule)
    {
        switch (raw)
        {
            case "not_scanned_library": rule = FileActionRule.NotScannedLibrary; return true;
            case "subject_outside_root": rule = FileActionRule.SubjectOutsideRoot; return true;
            case "traversal": rule = FileActionRule.Traversal; return true;
            case "missing_target": rule = FileActionRule.MissingTarget; return true;
            case "invalid_name": rule = FileActionRule.InvalidName; return true;
            case "outside_root": rule = FileActionRule.OutsideRoot; return true;
            case "symlink_escape": rule = FileActionRule.SymlinkEscape; return true;
            case "exempt_root": rule = FileActionRule.ExemptRoot; return true;
            case "same_as_source": rule = FileActionRule.SameAsSource; return true;
            case "target_not_a_directory": rule = FileActionRule.TargetNotADirectory; return true;
            case "target_exists": rule = FileActionRule.TargetExists; return true;
            case "nothing_to_retag": rule = FileActionRule.NothingToRetag; return true;
            case "symlinked_target": rule = FileActionRule.SymlinkedTarget; return true;
            case "cross_device": rule = FileActionRule.CrossDevice; return true;
            case "leftover_backup": rule = FileActionRule.LeftoverBackup; return true;
            default: rule = default; return false;
        }
    }
}
