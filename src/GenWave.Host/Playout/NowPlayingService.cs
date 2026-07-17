using System.Collections.Concurrent;

namespace GenWave.Host.Playout;

/// <summary>
/// Thread-safe per-station in-memory store of the current on-air snapshot. Updated by
/// <see cref="PlayoutFeederService"/> after each tick — no engine telnet calls are issued at
/// read time. Registered as a singleton.
/// </summary>
public sealed class NowPlayingService
{
    readonly ConcurrentDictionary<string, NowPlayingSnapshot> snapshots = new();

    /// <summary>
    /// Replaces the stored snapshot for the given station. Called from the feeder timer loop;
    /// only null-returning ticks (cold-start, no engine reply) are excluded.
    /// </summary>
    public void Update(string stationId, NowPlayingSnapshot snapshot)
        => snapshots[stationId] = snapshot;

    /// <summary>
    /// Returns the most-recent snapshot for the station, or null if the feeder has not
    /// completed its first tick yet (cold-start).
    /// </summary>
    public NowPlayingSnapshot? GetSnapshot(string stationId)
        => snapshots.TryGetValue(stationId, out var s) ? s : null;
}
