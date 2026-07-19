using System.Collections.Concurrent;
using System.Globalization;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Playout;

/// <summary>
/// SPEC F66.2-F66.4 — recovers <c>durationMs</c> for engine-initiated plays (safe rotation, restart
/// survivors) that never carry it on the Liquidsoap annotate line, but that the catalog knows.
/// Triggered from <see cref="NowPlayingService.Update"/> — the one seam every published snapshot
/// flows through — so it fires with or without <see cref="PlayoutFeederService"/> running.
/// <para>
/// One memoized, UNSCOPED by-id catalog read per media id (an aired-fact lookup: the track already
/// aired, so scope — a selection-time concern — does not apply). A hit patches both the now-playing
/// snapshot, via the caller-supplied <c>patchNowPlaying</c> callback so <see cref="NowPlayingService"/>
/// alone decides whether the airing it applies to is still current, and the matching
/// <see cref="PlayHistoryService"/> entry. A miss is memoized too (a nonexistent row will not
/// suddenly appear); a thrown catalog read is logged and NOT memoized, so the next airing of the same
/// id gets a fresh attempt once the catalog recovers. Nothing here ever throws into the playout/API
/// path (F66.4) — <see cref="GenWave.Core.Playout.PlayoutFeeder"/> itself stays DB-free (F16.6).
/// </para>
/// </summary>
public sealed class DurationRehydrator(
    IMediaCatalog catalog,
    PlayHistoryService history,
    ILogger<DurationRehydrator> logger)
{
    // Bounds unbounded growth across a long-running process. A full clear occasionally re-reads an
    // id that was already resolved, which is harmless (one extra catalog round-trip) — simpler than
    // an LRU for a component whose entire job is "don't hammer the DB every 3s tick".
    const int MaxMemoEntries = 512;

    readonly ConcurrentDictionary<string, Task<int?>> memoByMediaId = new(StringComparer.Ordinal);

    /// <summary>
    /// Called for every snapshot <see cref="NowPlayingService.Update"/> publishes. A no-op — no
    /// catalog read at all — unless the snapshot is real, carries a numeric media id, and is still
    /// missing its duration. Fire-and-forget: the lookup and patch run on a discarded background
    /// task so the synchronous publish path is never slowed or broken by a catalog call.
    /// </summary>
    public void OnPublished(string stationId, NowPlayingSnapshot snapshot, Action<string, int> patchNowPlaying)
    {
        if (snapshot.IsDrain || snapshot.DurationMs is not null) return;
        if (snapshot.MediaId is not { } mediaId ||
            !long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return;

        _ = RehydrateAsync(stationId, mediaId, snapshot.StartedAt, patchNowPlaying);
    }

    async Task RehydrateAsync(
        string stationId, string mediaId, DateTimeOffset startedAt, Action<string, int> patchNowPlaying)
    {
        try
        {
            var durationMs = await LookupAsync(mediaId);
            if (durationMs is null) return;

            patchNowPlaying(mediaId, durationMs.Value);
            history.TryPatchDuration(stationId, mediaId, startedAt, durationMs.Value);
        }
        catch (Exception ex)
        {
            // Belt-and-suspenders: FetchAndMemoizeAsync already isolates catalog failures. Nothing
            // on this path may ever reach NowPlayingService.Update's caller (F66.4).
            logger.LogWarning(ex, "Duration rehydration failed unexpectedly for media {MediaId}", mediaId);
        }
    }

    Task<int?> LookupAsync(string mediaId)
    {
        if (memoByMediaId.Count > MaxMemoEntries) memoByMediaId.Clear();
        return memoByMediaId.GetOrAdd(mediaId, FetchAndMemoizeAsync);
    }

    async Task<int?> FetchAndMemoizeAsync(string mediaId)
    {
        try
        {
            // CancellationToken.None: this fetch is memoized and shared across whichever Update()
            // call happens to trigger it first — it has no single owning request or tick to bind a
            // token to, and it must complete on its own regardless of the caller that started it.
            var reference = await catalog.GetByIdUnscopedAsync(mediaId, CancellationToken.None);
            return reference?.DurationMs;
        }
        catch (Exception ex)
        {
            // A transient catalog failure must not stick forever: un-memoize so the next airing of
            // this id gets a fresh attempt once the catalog recovers (F66.4 — never throws, never
            // fabricates).
            memoByMediaId.TryRemove(mediaId, out _);
            logger.LogDebug(ex, "Duration rehydration catalog read failed for media {MediaId}", mediaId);
            return null;
        }
    }
}
