using System.Collections.Concurrent;

namespace GenWave.Host.Playout;

/// <summary>
/// Thread-safe per-station in-memory store of the current on-air snapshot. Updated by
/// <see cref="PlayoutFeederService"/> after each tick — no engine telnet calls are issued at
/// read time. Registered as a singleton.
/// <para>
/// <see cref="Update"/> is the one seam every published snapshot flows through (SPEC F66.2), so it
/// is also where <see cref="DurationRehydrator"/> is invoked: an optional, fire-and-forget hook —
/// this class stays a plain store with no catalog/history dependency of its own, and works
/// identically whether or not a rehydrator is wired in (e.g. tests that construct it directly).
/// </para>
/// </summary>
public sealed class NowPlayingService(DurationRehydrator? rehydrator = null)
{
    readonly ConcurrentDictionary<string, NowPlayingSnapshot> snapshots = new();

    /// <summary>
    /// Replaces the stored snapshot for the given station. Called from the feeder timer loop;
    /// only null-returning ticks (cold-start, no engine reply) are excluded. Also the trigger seam
    /// for duration rehydration (SPEC F66.2) — a no-op when the published snapshot already has a
    /// duration, is a drain token, or carries a non-numeric (e.g. <c>tts:*</c>) id.
    /// </summary>
    public void Update(string stationId, NowPlayingSnapshot snapshot)
    {
        snapshots[stationId] = snapshot;
        rehydrator?.OnPublished(stationId, snapshot,
            (mediaId, durationMs) => PatchIfSameAiring(stationId, mediaId, snapshot.StartedAt, durationMs));
    }

    /// <summary>
    /// Returns the most-recent snapshot for the station, or null if the feeder has not
    /// completed its first tick yet (cold-start).
    /// </summary>
    public NowPlayingSnapshot? GetSnapshot(string stationId)
        => snapshots.TryGetValue(stationId, out var s) ? s : null;

    // Applies a rehydrated duration only if the stored snapshot is still the same airing
    // (mediaId + startedAt) the rehydration was triggered for — a newer track that came on-air
    // while the catalog read was in flight must never be clobbered by a stale patch.
    void PatchIfSameAiring(string stationId, string mediaId, DateTimeOffset startedAt, int durationMs)
    {
        if (!snapshots.TryGetValue(stationId, out var current)) return;
        if (current.MediaId != mediaId || current.StartedAt != startedAt) return;

        snapshots.TryUpdate(stationId, current with { DurationMs = durationMs }, current);
    }
}
