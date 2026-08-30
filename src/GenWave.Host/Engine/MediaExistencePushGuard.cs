using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Engine;

/// <summary>
/// Push-honesty guard (gh-#612): declines a push whose file does not exist on disk, instead of
/// letting the engine fail it silently. Liquidsoap's <c>q.push</c> allocates the RID BEFORE
/// resolving the URI, so a push of a nonexistent path returns a success-shaped numeric reply and
/// then dies engine-side at <c>[request:3] Nonexistent file or ill-formed URI</c> — severity 3,
/// containing neither "error" nor "fail", invisible to every log sweep. The api believed the queue
/// was fed while safe rotation covered the hole; on the 2026-08-22 dev-box incident that shape ran
/// silently for seven days (261 rejects/24h — the gh-#610 root cause).
/// <para>
/// The api and the engine share the same media mounts (<c>/media</c>, <c>/tts</c>, <c>/authored</c>
/// ride the same volumes on both containers), so a local <see cref="File.Exists(string?)"/> answers
/// the exact question the engine is about to ask. A declined push returns <see langword="null"/>
/// per the <see cref="ILiquidsoapControl.PushAsync"/> contract — the feeder ends the chain and
/// re-selects next tick, which is the same recovery the silent failure eventually forced, minus the
/// silence and the safe-rotation air time. One WARN per declined push names the path and the id.
/// </para>
/// <para>
/// Only a fully-qualified locator is checked: today every production locator is an absolute
/// container path, but a future URI-shaped locator (the engine log's own "or ill-formed URI" arm)
/// is not this guard's question to answer — it passes through to the engine untouched rather than
/// being declined over a shape <see cref="File.Exists(string?)"/> was never fit to judge.
/// </para>
/// <para>
/// <b>A decline also reports to the Gardener's dead_file queue</b> (SPEC F153.4; STORY-375; PLAN
/// T373): fire-and-forget, AFTER the WARN above and the decline itself, via
/// <see cref="BeginReportMissing"/> — see that method's own remarks for the shape and why a
/// reporter failure can never delay this method's return.
/// </para>
/// </summary>
sealed class MediaExistencePushGuard(
    ILiquidsoapControl inner, IDeadFileReporter reporter, ILogger<MediaExistencePushGuard> log)
    : ILiquidsoapControl
{
    public Task<string?> OnAirNewestAsync(CancellationToken ct) => inner.OnAirNewestAsync(ct);

    public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct) => inner.MetadataAsync(rid, ct);

    public Task<EnginePushResult?> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
    {
        if (Path.IsPathFullyQualified(item.Locator) && !File.Exists(item.Locator))
        {
            log.LogWarning(
                "Declined push of {MediaId} ('{Title}') — file does not exist: {Locator}. The engine "
                + "would have accepted the push and killed the request silently at resolution; the "
                + "feeder will re-select next tick. A ready catalog row naming a missing file usually "
                + "means the library and the disk have diverged (gh-#612; see also gh-#611).",
                item.MediaId, item.Title, item.Locator);

            BeginReportMissing(item.MediaId);

            return Task.FromResult<EnginePushResult?>(null);
        }

        return inner.PushAsync(item, gainDb, ct);
    }

    /// <summary>
    /// Fire-and-forget hook into <see cref="IDeadFileReporter"/> (SPEC F153.4, T373) — mirrors
    /// <c>Playout.DurationRehydrator.OnPublished</c>'s own "discard-invoked async method, its own
    /// try/catch, <see cref="CancellationToken.None"/>" shape rather than a bounded
    /// channel+drain-service pair (<c>Playout.MediaRotationEventSink</c>/
    /// <c>MediaRotationDrainService</c>'s own precedent, T355): a missing file at push time is a
    /// rare event compared to "every track start", so an extra queue/hosted-service pair here would
    /// be ceremony this call site does not earn — a plain discarded task the caller never awaits is
    /// the lighter, equally-safe shape DurationRehydrator already established one seam over.
    ///
    /// <para>
    /// <see cref="CancellationToken.None"/>, deliberately NOT <see cref="PushAsync"/>'s own
    /// <c>ct</c>: the decline above has already happened by the time this is called, so a caller
    /// that goes on to cancel its own push request (the feeder re-selecting next tick) must never
    /// cancel a report already under way — the same reasoning
    /// <c>DurationRehydrator.FetchAndMemoizeAsync</c>'s own remarks give for its identical choice.
    /// </para>
    ///
    /// <para>
    /// A <c>tts:*</c>-prefixed or otherwise non-numeric <see cref="MediaItem.MediaId"/> names no
    /// <c>library.media</c> row this queue can reconcile against — the same numeric-id
    /// discrimination <c>Playout.MusicAiring.IsMusicMediaId</c> applies, both now sharing ONE
    /// implementation (T373 review LOW-1): <see cref="MusicMediaId.TryParse"/>, in the root
    /// <c>GenWave.Host</c> namespace rather than on <c>Playout.MusicAiring</c> itself — that type's
    /// own remarks explain why (<c>Playout</c> already depends on <c>Engine</c>, so a direct call
    /// from here into <c>Playout.MusicAiring</c> would close an L10 namespace cycle). This call site
    /// needs the parsed value itself, which <c>Playout.MusicAiring.IsMusicMediaId</c>'s bool-only
    /// return discards — <see cref="MusicMediaId.TryParse"/> hands it back directly.
    /// </para>
    /// </summary>
    void BeginReportMissing(string mediaId)
    {
        if (!MusicMediaId.TryParse(mediaId, out var id)) return;

        _ = ReportInBackgroundAsync(id);
    }

    async Task ReportInBackgroundAsync(long mediaId)
    {
        try
        {
            await reporter.ReportMissingAsync(mediaId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // SPEC F153.4: a reporter failure WARNs and never delays the feeder — PushAsync has
            // already returned its decline by the time this task even runs. T373 review LOW-5: the
            // message names the reporter seam itself (nameof(IDeadFileReporter)), not just "a
            // report failed", so a WARN sweep can filter on the seam by name.
            log.LogWarning(
                ex, "{Reporter} report failed for media {MediaId}", nameof(IDeadFileReporter), mediaId);
        }
    }
}
