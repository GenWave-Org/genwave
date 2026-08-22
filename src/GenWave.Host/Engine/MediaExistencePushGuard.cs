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
/// </summary>
sealed class MediaExistencePushGuard(ILiquidsoapControl inner, ILogger<MediaExistencePushGuard> log)
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
            return Task.FromResult<EnginePushResult?>(null);
        }

        return inner.PushAsync(item, gainDb, ct);
    }
}
