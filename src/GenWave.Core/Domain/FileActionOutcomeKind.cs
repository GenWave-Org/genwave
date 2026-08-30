namespace GenWave.Core.Domain;

/// <summary>
/// The closed set of results <see cref="Abstractions.IFileActionExecutor"/> reports after attempting
/// one file action (SPEC F154.4, F154.6-F154.8; STORY-379; PLAN T380, gh-#529). Every value maps to
/// its own <c>library.file_action.outcome</c> wire token (lower-case, the same literal word) — the
/// executor's own concern, not a shared token map, since this enum has exactly one writer.
/// </summary>
public enum FileActionOutcomeKind
{
    /// <summary>The filesystem op and the database update + audit row landed together, in one
    /// transaction — the action is complete.</summary>
    Done,

    /// <summary>The row's <c>(xmin, path)</c> no longer matches the plan's own binding — nothing was
    /// touched (SPEC F154.5, STORY-379 AC7's executor half).</summary>
    Conflict,

    /// <summary>The re-probed target is occupied, or a move's destination directory is no longer a
    /// real directory — nothing was touched (SPEC F154.4). <see cref="FileActionOutcome.Rule"/> names
    /// which.</summary>
    Refused,

    /// <summary>The filesystem op succeeded but the database update failed — the filesystem op was
    /// reverted, and the row is exactly as it was before this attempt (SPEC F154.7).</summary>
    Reverted,

    /// <summary>The filesystem op itself failed (F154.4's own overwrite/cross-device refusal
    /// surfaces here), or a revert (see <see cref="Reverted"/>) itself failed. The latter is logged
    /// as a WARN naming the media id only, never a path.</summary>
    Failed,

    /// <summary>The scan gate could not be entered within its own bounded wait — nothing was
    /// touched.</summary>
    Busy,
}
