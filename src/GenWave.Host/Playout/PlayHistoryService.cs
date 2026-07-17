using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using GenWave.Host.Options;

namespace GenWave.Host.Playout;

/// <summary>
/// Thread-safe per-station play history ring buffer. Keeps the <c>N</c> most-recently-aired entries
/// per station (newest first). Entry lifetime spans two advances: when the next track airs, the
/// previous entry's <see cref="PlayHistoryEntry.EndedAt"/> is stamped with the new entry's
/// <see cref="PlayHistoryEntry.StartedAt"/>. Registered as a singleton.
/// </summary>
public sealed class PlayHistoryService
{
    // ConcurrentDictionary for the outer per-station maps: GetOrAdd is atomic, so two feeders
    // racing to register the same station can never corrupt the map or lose an entry.
    readonly ConcurrentDictionary<string, LinkedList<PlayHistoryEntry>> rings = new();
    readonly ConcurrentDictionary<string, object> locks = new();
    readonly IOptionsMonitor<AdminOptions> options;

    // Live capacity seam (SPEC F44.2, closes gitea-#197): Admin:PlayHistoryCapacity is advertised Live in
    // the settings allowlist, so capacity is read fresh from IOptionsMonitor<AdminOptions> at EVERY
    // Push() — never a boot-frozen field — so a live PUT /api/settings shrink trims the ring on the
    // very next push, with no api restart.
    public PlayHistoryService(IOptionsMonitor<AdminOptions> options)
    {
        this.options = options;
    }

    /// <summary>
    /// Push a new entry for the given station. Stamps <see cref="PlayHistoryEntry.EndedAt"/> on the
    /// previous entry (the one that was still on-air until now), then prepends the new entry. Evicts
    /// the oldest entry (or entries, if the ring just shrunk) when the ring exceeds the CURRENT
    /// capacity.
    /// </summary>
    public void Push(PlayHistoryEntry entry)
    {
        var capacity = options.CurrentValue.PlayHistoryCapacity;

        lock (LockFor(entry.StationId))
        {
            var ring = RingFor(entry.StationId);

            // Stamp endedAt on the previous entry (the track that just finished airing).
            if (ring.First is not null)
            {
                var prev = ring.First.Value;
                ring.RemoveFirst();
                ring.AddFirst(prev with { EndedAt = entry.StartedAt });
            }

            ring.AddFirst(entry);   // newest first

            while (ring.Count > capacity)
                ring.RemoveLast();
        }
    }

    /// <summary>Returns the history for a station, newest first. Empty list before any advances.</summary>
    public IReadOnlyList<PlayHistoryEntry> GetEntries(string stationId)
    {
        lock (LockFor(stationId))
        {
            return RingFor(stationId).ToList();
        }
    }

    LinkedList<PlayHistoryEntry> RingFor(string stationId)
        => rings.GetOrAdd(stationId, _ => new LinkedList<PlayHistoryEntry>());

    object LockFor(string stationId)
        => locks.GetOrAdd(stationId, _ => new object());
}
