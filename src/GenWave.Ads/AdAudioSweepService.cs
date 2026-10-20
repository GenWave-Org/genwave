namespace GenWave.Ads;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// Deletes ad audio nothing will air again (gh-#3). Two jobs, one hourly tick:
/// <list type="bullet">
/// <item>Ineligible ads-library rows past <see cref="AdsOptions.RetiredAudioRetentionDays"/> that no spot
/// still points at — a spot retired that long ago, or a render a re-render replaced. File first, then
/// row; a file that will not delete keeps its row for the next tick.</item>
/// <item>Preview files no spot has stamped (a re-preview overwrote the stamp) once they outlive
/// <see cref="AdsOptions.PreviewRetentionDays"/>. Stamped previews stay
/// <see cref="AdSpotLifecycleGuardianService"/>'s job.</item>
/// </list>
/// Only the ads library is swept. Safe segments and jingle packs are never auto-deleted — an operator
/// may have switched <c>eligible</c> off on purpose. Every path is re-asserted under its own root in the
/// SAME method that deletes it (the CodeQL strong-guard shape, <see cref="AdPreviewRoot.IsUnder"/>).
/// </summary>
sealed class AdAudioSweepService(
    IAuthoredMediaSweep mediaSweep,
    IAdAudioReferences references,
    ILibraryRepository libraries,
    IOptionsMonitor<AdsOptions> adsOptions,
    AdSpotLocatorRoots locatorRoots,
    TimeProvider timeProvider,
    ILogger<AdAudioSweepService> logger) : BackgroundService
{
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(SweepInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }
    }

    /// <summary>One sweep, internal so a spec can drive it without the timer. Never throws past the
    /// "caller cancelled" case.</summary>
    internal async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var options = adsOptions.CurrentValue;
            var audio = await SweepRetiredAudioAsync(now - TimeSpan.FromDays(options.RetiredAudioRetentionDays), options.LibraryName, ct);
            var previews = await SweepOrphanPreviewsAsync(now - TimeSpan.FromDays(options.PreviewRetentionDays), ct);

            // gh-#558 volume lesson: no line when the tick found nothing to do.
            if (audio > 0 || previews > 0)
                logger.LogInformation("Ad audio sweep: deleted audio={Audio} previews={Previews}", audio, previews);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad audio sweep failed; continuing on the next tick");
        }
    }

    async Task<int> SweepRetiredAudioAsync(DateTimeOffset cutoff, string libraryName, CancellationToken ct)
    {
        var library = await libraries.GetByNameAsync(libraryName, ct);
        if (library is null)
            return 0;

        var candidates = await mediaSweep.ListIneligibleAsync(library.Id, cutoff, ct);
        if (candidates.Count == 0)
            return 0;

        // Read AFTER the candidates: a spot that adopts a candidate between the two reads is still seen.
        var inUse = await references.ListMediaIdsInUseAsync(cutoff, ct);
        var adsRoot = AdRenderService.ResolveAdsRoot(locatorRoots);
        var deleted = 0;
        foreach (var media in candidates)
        {
            if (inUse.Contains(media.Id))
                continue;

            var target = Path.GetFullPath(media.Path);
            if (!AdPreviewRoot.IsUnder(adsRoot, target))
            {
                logger.LogWarning("Ad audio sweep skipped media {Id}: its path is outside the ads root", media.Id);
                continue;
            }

            try
            {
                File.Delete(target);
            }
            catch (DirectoryNotFoundException)
            {
                // already gone with its directory — the row is all that is left
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Ad audio sweep could not remove the file for media {Id}", media.Id);
                continue;
            }

            if (await mediaSweep.DeleteIneligibleAsync(media.Id, ct))
                deleted++;
        }

        return deleted;
    }

    async Task<int> SweepOrphanPreviewsAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var previewRoot = AdPreviewRoot.Resolve(locatorRoots);
        if (!Directory.Exists(previewRoot))
            return 0;

        var stamped = await references.ListPreviewPathsAsync(ct);
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(previewRoot))
        {
            var target = Path.GetFullPath(file);
            if (!AdPreviewRoot.IsUnder(previewRoot, target)
                || stamped.Contains(target)
                || File.GetLastWriteTimeUtc(target) >= cutoff.UtcDateTime)
                continue;

            try
            {
                File.Delete(target);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Ad audio sweep could not remove an orphan preview file");
            }
        }

        return deleted;
    }
}
