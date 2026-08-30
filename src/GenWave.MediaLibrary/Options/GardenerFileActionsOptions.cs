using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Options;

/// <summary>
/// The Library Gardener's one destructive-write gate (SPEC F154.1-F154.2, F155.1): nested under
/// <see cref="GardenerOptions.FileActions"/> so the bound shape matches the SPEC key literally
/// (<c>Gardener:FileActions:Enabled</c>, env <c>Gardener__FileActions__Enabled</c>) rather than a
/// flattened <c>Gardener:FileActionsEnabled</c>. Split into its own file — the house one-type-
/// per-file rule — even though it exists to back exactly one property today.
/// </summary>
public sealed class GardenerFileActionsOptions
{
    /// <summary>
    /// Off ⇒ <c>GardenerController</c>'s <c>file-actions/dry-run</c>/<c>confirm</c> endpoints 404
    /// and the Gardener page shows how to turn them on (F154.2); a shipped appliance owner opts in
    /// explicitly. Default <see langword="false"/> — fail-closed on a stranger's NAS (Dean's
    /// standing design preference; ARCHITECTURE.md "File actions: the first jail, the purge
    /// posture, the audit"). No <c>[Range]</c>/annotation of its own: a plain bool has nothing for
    /// <c>ValidateDataAnnotations()</c> to enforce, so the "does not recurse into nested option
    /// classes" boot-floor caveat (<see cref="GardenerOptions"/>'s own remarks) costs nothing here.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How long <c>Garden.FileActions.FileActionExecutor</c> waits to enter the shared
    /// <c>IScanGate</c> before reporting <c>FileActionOutcomeKind.Busy</c> (SPEC F154.6; STORY-379;
    /// PLAN T380). Default 30 seconds, range 1-300 (this task's own choice — SPEC leaves the bound
    /// unstated): long enough to ride out one ordinary scan tick over a homelab-sized library
    /// without leaving an admin request hanging for minutes. <c>[Range]</c> is documentation only
    /// here, the same nested-class caveat <see cref="Enabled"/>'s own remarks give — the executor
    /// clamps this to 1-300 itself before use (T380 review N5), the same live-value defence
    /// <c>ScanService.CurrentScanInterval</c>'s own floor already establishes for a value that could
    /// otherwise slip past boot validation via a live settings write.
    /// </summary>
    [Range(1, 300)]
    public int GateTimeoutSeconds { get; set; } = 30;
}
