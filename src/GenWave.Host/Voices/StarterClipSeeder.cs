using System.Diagnostics;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Tts;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Voices;

/// <summary>
/// Seeds the starter clips on boot (SPEC F219.14, PLAN T757): for each manifest entry, copies its WAV into the clips folder when
/// the file is missing and inserts its <c>source='starter'</c> row when the slug is free. Each slug is recorded in the clips folder's <c>.starter-seeded</c> marker once handled, so a starter the admin deletes is not
/// seeded again (a recreated volume loses the marker and gets its files back). Never overwrites a file or a
/// row (so an upload that took a starter's slug is left alone). A failure is a warning, never a boot failure.
/// </summary>
public sealed class StarterClipSeeder(
    IVoiceClipStore store,
    VoiceClipFolder folder,
    IOptionsMonitor<TtsOptions> ttsOptions,
    ILogger<StarterClipSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.StarterClipsRoot));
            var manifestPath = Path.Combine(root, StarterClipManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                logger.LogWarning("Starter voice clips: no {Manifest} in {Root}; none seeded", StarterClipManifest.FileName, root);
                return;
            }

            var seeded = folder.ReadSeededStarterSlugs();
            foreach (var entry in StarterClipManifest.Parse(await File.ReadAllTextAsync(manifestPath, ct)))
            {
                if (!seeded.Contains(entry.Slug) && await SeedOneAsync(entry, root, ct))
                    folder.MarkStarterSeeded(entry.Slug);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Starter voice clips: seeding failed; the station starts without them");
        }
    }

    async Task<bool> SeedOneAsync(StarterClipEntry entry, string root, CancellationToken ct)
    {
        try
        {
            var source = Path.GetFullPath(Path.Combine(root, entry.File));
            if (!VoiceClipFolder.IsUnderCanonicalRoot(source, root))
                throw new UnreachableException("A starter clip file resolved outside the starter folder.");

            if (!File.Exists(source))
            {
                logger.LogWarning("Starter voice clip file missing slug={Slug}; skipped", LogSanitize.Strip(entry.Slug));
                return false;
            }

            if (!folder.Exists(entry.Slug) && CopyIn(source, entry.Slug))
                logger.LogInformation("Starter voice clip copied slug={Slug}", LogSanitize.Strip(entry.Slug));

            var row = await store.InsertAsync(
                new NewVoiceClip(entry.Slug, entry.Label, entry.File, entry.DurationMs, VoiceClipSources.Starter, entry.Credit), ct);
            if (row is not null)
                logger.LogInformation("Starter voice clip seeded slug={Slug}", LogSanitize.Strip(entry.Slug));

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Starter voice clip failed slug={Slug}; skipped", LogSanitize.Strip(entry.Slug));
            return false;
        }
    }

    bool CopyIn(string source, string slug)
    {
        var staging = folder.NewStagingPath();
        try
        {
            File.Copy(source, staging, overwrite: true);
            return folder.Publish(staging, slug);
        }
        finally
        {
            File.Delete(staging);
        }
    }
}
