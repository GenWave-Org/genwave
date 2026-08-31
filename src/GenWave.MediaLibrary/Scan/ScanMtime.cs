namespace GenWave.MediaLibrary.Scan;

/// <summary>
/// The scan's own mtime precision (SPEC F154.6, F154.8; STORY-379; PLAN T380, gh-#529) — truncated to
/// whole seconds so a stat round-trips through <c>timestamptz</c> exactly and never spuriously
/// re-triggers a scan's own "changed" classification on sub-second precision differences. Extracted
/// out of <c>ScanService</c> (its original, sole owner) so <c>Garden.FileActions.FileActionExecutor</c>
/// can re-stat a file after a filesystem write with the IDENTICAL rule the next scan tick will judge
/// it by — a file action's own stat and the scan's own stat must never disagree on what "unchanged"
/// means (F154.6: "the next scan classifies the row unchanged").
/// </summary>
static class ScanMtime
{
    internal static DateTime TruncateToSeconds(DateTime t) =>
        new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
