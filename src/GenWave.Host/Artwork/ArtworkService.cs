using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Host.Options;

namespace GenWave.Host.Artwork;

/// <summary>
/// Extracts a track's embedded cover art via ffmpeg, downscaled to ≤500px JPEG, disk-cached once
/// per artwork token under <see cref="ArtworkOptions.CacheDir"/> (SPEC F88.3, STORY-222, PLAN
/// T84) — the same check-exists / render-to-scratch / atomic-move idiom
/// <c>GenWave.Tts.TtsSegmentSource</c> uses for its own cache.
/// <para>
/// A negative result (no embedded art, or ffmpeg fails to extract one) is deliberately NEVER
/// written to disk: a track that has no cover shouldn't leave a tombstone file behind, and every
/// artless track sharing the disk cache would otherwise need its own dummy entry. Instead a
/// miss is remembered in <see cref="knownArtless"/>, a small in-process set, so a repeatedly
/// polled artless token doesn't re-invoke ffmpeg on every spectator request. That set is bounded
/// — <see cref="MaxKnownArtless"/> entries — by clearing it outright rather than tracking any
/// per-entry eviction order: unbounded growth is never acceptable (every distinct token an
/// attacker or a large library can mint is a potential entry), but an occasional cleared miss is
/// cheap to rediscover (one ffmpeg run landing on the same miss), so no LRU/TTL machinery earns
/// its keep here (KISS).
/// </para>
/// </summary>
public sealed class ArtworkService(IOptionsMonitor<ArtworkOptions> options, ILogger<ArtworkService> logger)
{
    /// <summary>SPEC F88.3: embedded art is downscaled to at most this many pixels on its long side.</summary>
    const int MaxDimensionPx = 500;

    /// <summary>Bound on <see cref="knownArtless"/> before it is cleared outright (see class remarks).</summary>
    const int MaxKnownArtless = 10_000;

    /// <summary>
    /// A single ffmpeg invocation here only ever extracts one already-decoded frame — this is a
    /// generous defensive ceiling against a corrupt/hostile-shaped file hanging the process, not a
    /// budget any legitimate extraction should approach. This is an anonymous, unauthenticated
    /// endpoint (SPEC F88.3), so a bounded worst case matters even though the input path itself is
    /// always a trusted library row, never request-supplied.
    /// </summary>
    static readonly TimeSpan FfmpegTimeout = TimeSpan.FromSeconds(10);

    readonly ConcurrentDictionary<string, byte> knownArtless = new();

    /// <summary>
    /// Returns the path to <paramref name="token"/>'s cached ≤500px JPEG, extracting it from
    /// <paramref name="mediaPath"/>'s embedded art on first need. Returns <see langword="null"/>
    /// when the track has no embedded art or extraction otherwise fails — the caller (SPEC
    /// F88.3) falls back to the station icon in that case, exactly as it does for an unknown
    /// token.
    /// </summary>
    public async Task<string?> GetOrExtractAsync(string token, string mediaPath, CancellationToken ct)
    {
        if (knownArtless.ContainsKey(token))
            return null;

        var cacheDir = options.CurrentValue.CacheDir;
        var cachedPath = Path.Combine(cacheDir, $"{token}.jpg");
        if (File.Exists(cachedPath))
            return cachedPath;

        Directory.CreateDirectory(cacheDir);
        var scratchPath = Path.Combine(cacheDir, $"{token}.{Guid.NewGuid():N}.tmp");

        try
        {
            await ExtractAsync(mediaPath, scratchPath, ct);
            File.Move(scratchPath, cachedPath, overwrite: true);
            return cachedPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Media id never appears here, and this is Debug, not Warning+ — the house rule
            // (IcecastListenerStatsSource precedent). The TOKEN is deliberately absent too
            // (CodeQL cs/log-forging on PR #124): it is the one request-derived value in this
            // method, and the DB-sourced media path already identifies the row — logging zero
            // user-derived bytes beats sanitizing them.
            logger.LogDebug(ex, "Artwork extraction failed ({MediaPath})", mediaPath);
            RememberArtless(token);
            return null;
        }
        finally
        {
            DeleteIfExists(scratchPath);
        }
    }

    void RememberArtless(string token)
    {
        if (knownArtless.Count > MaxKnownArtless)
            knownArtless.Clear();

        knownArtless[token] = 0;
    }

    static async Task ExtractAsync(string mediaPath, string outputPath, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(FfmpegTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var p = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList =
            {
                "-nostdin", "-y", "-hide_banner", "-loglevel", "error",
                "-i", mediaPath,
                "-an", "-map", "0:v:0", "-frames:v", "1",
                "-vf", $"scale='min({MaxDimensionPx},iw)':'min({MaxDimensionPx},ih)':force_original_aspect_ratio=decrease:force_divisible_by=2",
                "-f", "mjpeg",
                outputPath,
            }
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        string stderr;
        try
        {
            stderr = await p.StandardError.ReadToEndAsync(linkedCts.Token);
            await p.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our own extraction timeout fired, not the caller's — kill the hung process and
            // surface it as an ordinary extraction failure (the caller falls back to the station
            // icon), same as a non-zero exit or a missing video stream.
            await KillAndWaitForExitAsync(p);
            throw new InvalidOperationException($"ffmpeg artwork extraction exceeded {FfmpegTimeout}.");
        }
        catch (OperationCanceledException)
        {
            // The real caller cancelled (e.g. the spectator request was aborted) — confirm the
            // process is dead before propagating, same reasoning as FfmpegAudioMixer.
            await KillAndWaitForExitAsync(p);
            throw;
        }

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {p.ExitCode}: {stderr}");
    }

    /// <summary>Terminates <paramref name="p"/> and waits (uncancellably) for the OS to confirm it
    /// has actually exited — a cancelled awaiter does not stop the underlying process.</summary>
    static async Task KillAndWaitForExitAsync(Process p)
    {
        try
        {
            if (!p.HasExited)
                p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and Kill() — nothing left to terminate.
        }

        await p.WaitForExitAsync(CancellationToken.None);
    }

    static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort scratch cleanup; a locked/undeletable temp file is not worth failing
            // (or masking) an otherwise-successful extraction over.
        }
    }

}
