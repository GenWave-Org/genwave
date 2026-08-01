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
    ActivePersonaCorrectionsCache personaCorrections,
    IOptionsMonitor<TtsOptions> options,
    ILogger<TtsSegmentSource> logger,
    IStationEventSink? events = null) : ITtsSegmentSource
{
    // SegmentGenerated publish seam (gitea-#246); no-op unless the host binds a real sink.
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;
    // Fresh-per-airing (LLM-authored) blurb audio lands here instead of the station's forever-cache
    // root, so it can be swept without touching templated kinds' stable (text,voice) cache (F34.6).
    // SignOff/SignOn (F92.4, F92.5) never reach this far on a template-fallback miss at all — see the
    // WARN+null guard right after copyWriter.WriteAsync below, which drops that render before a cache
    // path is even chosen — so by the time isBlurb is computed, copy.FreshPerAiring is the single
    // correct test for every kind reaching this point, handoff kinds included.
    const string BlurbsDirName = "blurbs";

    readonly ConcurrentDictionary<string, CuePoints?> cueCache = new();

    // Bumped whenever the persona/station MERGE ALGORITHM changes — never on a rule-CONTENT change,
    // which corrections.ContentHash / personaCorrections.ContentHash below already cover. The T136
    // precedence flip (F97.4, station-wins -> card-wins) is exactly this case: for an UNCHANGED
    // (station rules, card rules) pair the two content fingerprints are byte-identical before and
    // after the flip, yet the rendered audio is not — SpeechCorrectionProvider.BuildMerged now
    // resolves the very same overlap differently. Without a term that reacts to the ALGORITHM, not
    // just the rules, an evergreen (FreshPerAiring:false) StationId/LeadIn/BackAnnounce clip already
    // sitting in the named-volume cache would keep airing the pre-flip pronunciation forever, since
    // RenderAsync's file-exists short-circuit below never reaches the synthesizer (and so never
    // reaches SpeechCorrectionProvider.BuildMerged) again for that key. Bump this string on any
    // future merge-policy change (see PersonaOverStationMerge). Internal, not private: the
    // ScenarioMergePolicyVersionIsPartOfTheCacheKey spec (Story005) reads it to pin the hash
    // FORMULA — not this value — rather than duplicating it as a literal that could drift.
    internal const string MergePolicyVersion = "f97.4";

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

            // Design ruling, spec-cited (T123 review finding): a handoff piece must NEVER air
            // non-LLM-authored copy. F92.4's ladder is "two-piece -> whichever piece rendered ->
            // clean cut" — there is no "templated piece" rung — and F92.5 states handoff pieces ARE
            // LLM-authored blurbs, full stop. copy.FreshPerAiring false on a SignOff/SignOn render
            // means every writer in the chain missed: LlmCopyWriter's own three degrade paths
            // (disabled endpoint, timeout/non-2xx/connect, empty-or-over-length after cleanup) AND
            // DegradationGatedCopyWriter routing straight to TemplateCopyWriter — unconditionally in
            // Hard mode, or off an unclaimed Soft cadence slot — bypassing LlmCopyWriter entirely.
            // Every one of those returns template copy rather than throwing (ISegmentCopyWriter's own
            // never-throws contract), which is exactly why PatterTemplateRenderer still needs correct
            // SignOff/SignOn arms — they just must never reach air. One WARN, then null:
            // ITtsSegmentSource already allows null-never-throws, and T124's boundary producer treats
            // a null piece exactly like F92.4's "whichever piece rendered airs (else clean cut)".
            if (request.Kind is SegmentKind.SignOff or SegmentKind.SignOn && !copy.FreshPerAiring)
            {
                logger.LogWarning(
                    "Handoff copy for {Kind} on station {StationId} was not LLM-authored (writer " +
                    "degraded to template) — dropping this piece rather than airing non-LLM-authored " +
                    "handoff copy (SPEC F92.4, F92.5)",
                    request.Kind, request.StationId);
                return null;
            }

            // corrections.ContentHash (station rules) AND personaCorrections.ContentHash (the
            // active persona card's rules, SPEC F71.7) both fold into the cache key (SPEC F68.5) so
            // EITHER a corrections rebuild (PUT /api/settings), a card edit, or a
            // Station:Persona:ActiveId switch re-keys every subsequent cache lookup: the very next
            // render of the same (text, voice, station) misses, falls through to
            // synthesizer.SynthesizeAsync below (NormalizingTtsSynthesizer — the only place either
            // set of corrections actually applies, via SpeechCorrectionProvider.BuildMerged's
            // persona-over-station merge, F97.4), and lands under a new hash. This class never reads a
            // correction rule itself, only the two fingerprints saying "these are the rules in
            // effect on each side of the merge". MergePolicyVersion (see its own remarks above)
            // folds in alongside them for the third, orthogonal reason a render can change without
            // either fingerprint moving: the ALGORITHM that resolves an overlap between the two
            // rule sets changed, not the rules themselves.
            //
            // Ordering matters: RefreshIfStaleAsync is awaited BEFORE computing the hash below —
            // NOT left for NormalizingTtsSynthesizer's own call inside SynthesizeAsync to discover
            // first — so the key and the eventual render read the SAME generation of
            // personaCorrections in the common case (this cache is a DI singleton; its own refresh
            // is idempotent and gated, so NormalizingTtsSynthesizer's later call is just a fast
            // already-fresh no-op). Reversing this order would let the key capture the PRE-refresh
            // snapshot while the render — on a cache miss — applies the POST-refresh one: a fresh
            // synthesis would then land under a hash that no longer matches what was actually
            // spoken, and the file would sit orphaned until the next render recomputes with the new
            // snapshot and re-hits it (self-healing next render — same accepted mid-render race
            // TtsSegmentSource already tolerates elsewhere, just moved to a different trigger).
            //
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
            //
            // Staleness bound inherited, honestly: personaCorrections.ContentHash can itself lag a
            // real card edit/persona switch by up to ActivePersonaCorrectionsCache.StalenessBound
            // (its own refresh is a bounded poll, not an instant subscription — see its class
            // remarks) — the cache key can never be MORE current than the rules it is keying on. A
            // station-only deployment (no active persona at all) is unaffected: personaCorrections
            // always folds to its own stable "no card corrections" sentinel there, so this term
            // never varies for a station running with no persona feature in play.
            await personaCorrections.RefreshIfStaleAsync(ct);
            var hash = ComputeHash(
                copy.Text, request.Voice, request.StationId, corrections.ContentHash, personaCorrections.ContentHash,
                MergePolicyVersion);
            var stationDir = Path.Combine(cfg.CacheRoot, request.StationId);
            // Plain FreshPerAiring is the whole test again (F34.6): the guard above already sent a
            // non-fresh SignOff/SignOn render home before this line, so nothing reaching here needs a
            // second, kind-based override to land in blurbs/ — a genuinely LLM-authored render of ANY
            // kind still does, an evergreen template render of any kind still doesn't.
            var isBlurb = copy.FreshPerAiring;
            var targetDir = isBlurb ? Path.Combine(stationDir, BlurbsDirName) : stationDir;
            var path = Path.Combine(targetDir, $"{hash}.{cfg.Format}");

            var fileExists = File.Exists(path);
            if (!fileExists)
            {
                Directory.CreateDirectory(targetDir);
                // Kind-aware overload (SPEC F70.3, STORY-191): this is the one caller that knows a
                // real SegmentKind — FallbackTtsSynthesizer reads it to consult Tts:EngineByKind.
                var synthPath = await synthesizer.SynthesizeAsync(
                    new TtsRenderContext(copy.Text, request.Voice, request.Kind), ct);
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

            // Opportunistic GC (F34.6): only after a render that actually landed in blurbs/ (fresh
            // copy — the only route left, now that a non-fresh handoff render never reaches this far
            // at all) — templated kinds' forever-cache is never touched. Best-effort; a sweep failure
            // must never fail a render that already succeeded.
            if (isBlurb)
                SweepBlurbs(targetDir, request.StationId);

            // Display title is the station name, NOT the spoken text (issue gitea-#154) — players would
            // otherwise show the whole patter script as the now-playing title. Artist credits the
            // active persona reading the patter when one is active, else the station name (SPEC
            // F39.2, gitea-#212): while a persona is on air it is that persona's voice reading the
            // DJ-spoken kinds (TimeDate, LeadIn, BackAnnounce) alike, so the credit follows it.
            // StationId is the exception (gh-#96): station imaging always arrives with the station's
            // own voice and PersonaName null, so its credit is the station name by construction. No
            // active persona falls back to the gitea-#192/gitea-#172 brand rule unchanged (artist = <Station Name>) —
            // without it every station ID / lead-in / back-announce rendered "Unknown artist" in
            // the admin UI's now-playing and play-history surfaces. This is per-airing state, not
            // cached content: the cache key below never includes PersonaName, so a cache-hit render
            // still carries whichever persona is CURRENTLY active (F39.3).
            // Render succeeded (cache hit or fresh synthesis) — publish before returning (gitea-#246).
            events.Publish(new SegmentGenerated($"tts:{hash}", request.Kind.ToString(), request.Voice));

            // DjName (gh-#259) carries the SPEAKER's persona name for Now Playing attribution —
            // request.PersonaName verbatim, no StationName fallback (unlike the Artist credit line
            // above): a station-voiced segment has no DJ of its own, and the Orchestrator stamps the
            // unit's show persona onto StationId segments itself. Per-airing state, same as Artist —
            // never part of the cache key.
            return new MediaItem(
                $"tts:{hash}", path, request.StationName, loudness,
                Artist: request.PersonaName ?? request.StationName, Cue: cuePoints, DurationMs: durationMs,
                DjName: request.PersonaName);
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

    static string ComputeHash(
        string text, string voice, string stationId, string correctionsContentHash, string personaCorrectionsContentHash,
        string mergePolicyVersion) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                text + "|" + voice + "|" + stationId + "|" + correctionsContentHash + "|" + personaCorrectionsContentHash +
                "|" + mergePolicyVersion)));
}
