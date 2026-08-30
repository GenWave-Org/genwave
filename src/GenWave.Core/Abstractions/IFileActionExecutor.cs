using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Executes a file action <see cref="IFileActionPlanner"/> already planned and a confirm request
/// re-presented (SPEC F154.4, F154.6-F154.8; STORY-379; PLAN T380, gh-#529) — the write half of the
/// jail, once T381's confirm endpoint has already re-verified the plan token itself. Holds
/// <see cref="IScanGate"/> for the whole attempt (F154.6: a scan and a file action never overlap),
/// re-checks the plan's own binding one more time (the TOCTOU gap between dry-run and confirm),
/// performs exactly one filesystem operation, then updates the catalog row and writes the audit row
/// in one transaction — reverting the filesystem operation if that transaction fails.
/// </summary>
public interface IFileActionExecutor
{
    /// <summary>
    /// Attempts <paramref name="plan"/>. <paramref name="planToken"/> is carried onto every audit row
    /// this attempt writes (SPEC F154.7) — it is never re-verified here; that is the caller's own
    /// job before this method is ever reached.
    /// </summary>
    Task<FileActionOutcome> ExecuteAsync(FileActionPlan plan, string planToken, CancellationToken ct);
}
