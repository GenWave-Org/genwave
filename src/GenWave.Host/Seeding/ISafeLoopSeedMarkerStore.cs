namespace GenWave.Host.Seeding;

/// <summary>
/// Reads and writes the one-shot boot-seed marker in <c>station.settings</c> (SPEC F27.6/F27.10,
/// STORY-080). Deliberately NOT <see cref="GenWave.Host.Configuration.IStationSettingsStore"/> —
/// that seam enforces <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/> on every
/// read and write, and the marker MUST NOT be allowlisted so <c>GET /api/settings</c> can never
/// return it (F27.10). This seam talks to the same <c>station.settings</c> table directly, scoped to
/// exactly one key that no other component ever reads or writes.
/// </summary>
public interface ISafeLoopSeedMarkerStore
{
    /// <summary>True once a prior boot completed the safe-loop seed successfully.</summary>
    Task<bool> ExistsAsync(CancellationToken ct);

    /// <summary>
    /// Records the seed as complete. Callers must only invoke this after every seed step (library,
    /// render, optional SafeScope overlay) has already succeeded (F27.6: "marker written only on
    /// success").
    /// </summary>
    Task MarkCompletedAsync(CancellationToken ct);
}
