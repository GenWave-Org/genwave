namespace GenWave.Tts;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;

public sealed class TtsSegmentSource(
    ISegmentCopyWriter copyWriter,
    ITtsSynthesizer synthesizer,
    ILoudnessAnalyzer analyzer,
    ICueAnalyzer cueAnalyzer,
    SpeechCorrectionProvider corrections,
    IOptionsMonitor<TtsOptions> options,
    ILogger<TtsSegmentSource> logger,
    IStationEventSink? events = null) : ITtsSegmentSource
{
    // SegmentGenerated publish seam (gitea-#246); no-op unless the host binds a real sink.
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;
    // Fresh-per-airing (LLM-authored) blurb audio lands here instead of the station's forever-cache
    // root, so it can be swept without touching templated kinds' stable (text,voice) cache (F34.6).
    const string BlurbsDirName = "blurbs";

    readonly ConcurrentDictionary<string, CuePoints?> cueCache = new();

    public async Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct)
    {
        try
        {
            // Read fresh per render — not a boot-frozen field — so Tts:BlurbRetentionHours
            // (SPEC F44.2, closes gitea-#197) is live for SweepBlurbs below. CacheRoot/Format are not
            // operator-editable (deployment topology, F44.4), so reading them from CurrentValue
            // instead of a frozen snapshot changes nothing observable for them.
            var cfg = options.CurrentValue;
            var copy = await copyWriter.WriteAsync(request, ct);
            // corrections.ContentHash folds into the cache key (SPEC F68.5) so a corrections
            // rebuild — triggered by a PUT /api/settings save — re-keys every subsequent cache
            // lookup: the very next render of the same (text, voice, station) misses, falls through
            // to synthesizer.SynthesizeAsync below (NormalizingTtsSynthesizer — the only place
            // corrections actually apply), and lands under a new hash. This class never reads a
            // correction rule itself, only the fingerprint saying "these are the rules in effect".
            // A deterministic content fingerprint — NOT SpeechCorrectionProvider.Version (a
            // process-local counter that resets to 0 at every construction) — is required here: the
            // TTS cache directory is a named Docker volume and its files are never swept on their
            // own (only blurbsDir entries are, see SweepBlurbs below), so it outlives any container
            // redeploy. A counter-based key would let a fresh process's version=0 collide with
            // orphaned pre-redeploy entries and serve stale pronunciation again; the same rules
            // always fold to the same fingerprint across restarts, and changed rules always fold to
            // a new one, so a redeploy can never accidentally resurrect a stale cache entry.
            // Without SOME such term here, an evergreen StationId/LeadIn/BackAnnounce clip
            // (FreshPerAiring:false, never GC'd) would keep airing the OLD pronunciation forever.
            // The file at the stale hash is simply orphaned, never rewritten or deleted — accepted
            // disk cost on the evergreen stationDir cache (a named volume with no retention sweep of
            // its own): correctness on the very next spoken line matters more here than reclaiming a
            // few stale audio files.
            var hash = ComputeHash(copy.Text, request.Voice, request.StationId, corrections.ContentHash);
            var stationDir = Path.Combine(cfg.CacheRoot, request.StationId);
            var targetDir = copy.FreshPerAiring ? Path.Combine(stationDir, BlurbsDirName) : stationDir;
            var path = Path.Combine(targetDir, $"{hash}.{cfg.Format}");

            var fileExists = File.Exists(path);
            if (!fileExists)
            {
                Directory.CreateDirectory(targetDir);
                var synthPath = await synthesizer.SynthesizeAsync(copy.Text, request.Voice, ct);
                File.Move(synthPath, path, overwrite: true);
            }

            var loudness = await analyzer.AnalyzeAsync(path, ct);

            CuePoints? cuePoints;
            if (fileExists && cueCache.TryGetValue(hash, out var cached))
            {
                cuePoints = cached;
            }
            else
            {
                cuePoints = await MeasureCueAsync(path, hash, ct);
            }

            // Duration is measured, never fabricated (SPEC F66.1): stamped from the cue analyzer's
            // CueOutSec — same derivation SafeSegmentAuthor.BuildInsert uses for authored segments —
            // and stays null when cue analysis failed (already logged in MeasureCueAsync above).
            // cuePoints covers BOTH the fresh-render and cache-hit paths above, so a cached segment's
            // cached cue points stamp the duration here too.
            var durationMs = cuePoints is not null
                ? (int?)Math.Round(cuePoints.CueOutSec * 1000.0, MidpointRounding.AwayFromZero)
                : null;

            // Opportunistic GC (F34.6): only after a successful fresh-copy render, and only inside
            // blurbs/ — templated kinds' forever-cache is never touched. Best-effort; a sweep failure
            // must never fail a render that already succeeded.
            if (copy.FreshPerAiring)
                SweepBlurbs(targetDir, request.StationId);

            // Display title is the station name, NOT the spoken text (issue gitea-#154) — players would
            // otherwise show the whole patter script as the now-playing title. Artist credits the
            // active persona reading the patter when one is active, else the station name (SPEC
            // F39.2, gitea-#212): while a persona is on air it is that persona's voice reading every kind
            // (StationId, TimeDate, LeadIn, BackAnnounce) alike, so the credit follows it. No active
            // persona falls back to the gitea-#192/gitea-#172 brand rule unchanged (artist = <Station Name>) —
            // without it every station ID / lead-in / back-announce rendered "Unknown artist" in
            // the admin UI's now-playing and play-history surfaces. This is per-airing state, not
            // cached content: the cache key below never includes PersonaName, so a cache-hit render
            // still carries whichever persona is CURRENTLY active (F39.3).
            // Render succeeded (cache hit or fresh synthesis) — publish before returning (gitea-#246).
            events.Publish(new SegmentGenerated($"tts:{hash}", request.Kind.ToString(), request.Voice));

            return new MediaItem(
                $"tts:{hash}", path, request.StationName, loudness,
                Artist: request.PersonaName ?? request.StationName, Cue: cuePoints, DurationMs: durationMs);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TTS render failed for {Kind}/{Voice}", request.Kind, request.Voice);
            return null;
        }
    }

    /// <summary>
    /// Deletes <paramref name="blurbsDir"/> entries whose last-write time is older than
    /// <see cref="TtsOptions.BlurbRetentionHours"/>. Never reaches outside <paramref name="blurbsDir"/>.
    /// Stops at the first delete failure (locked file, permission denied, lost race with a concurrent
    /// delete) and logs once — the next blurb render retries whatever is left (F34.6, AC4).
    /// </summary>
    void SweepBlurbs(string blurbsDir, string stationId)
    {
        try
        {
            if (!Directory.Exists(blurbsDir))
                return;

            // Read fresh at sweep time (SPEC F44.2) — never a boot-frozen field — so a live edit to
            // Tts:BlurbRetentionHours changes the retention horizon on the very next blurb render.
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(options.CurrentValue.BlurbRetentionHours);
            foreach (var entry in Directory.EnumerateFileSystemEntries(blurbsDir))
            {
                if (File.GetLastWriteTimeUtc(entry) < cutoff)
                    File.Delete(entry);
            }
        }
        catch (Exception ex)
        {
            // Deliberately broad: this GC step is opportunistic (SPEC F34.6) — a render that already
            // succeeded must return regardless of WHY the sweep couldn't finish (locked file, denied
            // permission, a race with a concurrent delete). The next blurb render retries.
            logger.LogWarning(ex, "Blurb retention sweep failed for station {StationId}; retrying on next blurb render", stationId);
        }
    }

    async Task<CuePoints?> MeasureCueAsync(string path, string hash, CancellationToken ct)
    {
        try
        {
            var result = await cueAnalyzer.AnalyzeAsync(path, ct);
            cueCache[hash] = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cue analysis failed for TTS clip {Hash}", hash);
            cueCache[hash] = null;
            return null;
        }
    }

    static string ComputeHash(string text, string voice, string stationId, string correctionsContentHash) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(text + "|" + voice + "|" + stationId + "|" + correctionsContentHash)));
}
