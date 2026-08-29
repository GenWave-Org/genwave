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
}
