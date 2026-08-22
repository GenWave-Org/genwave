using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Scan;

/// <summary>
/// Discovery: the cheap, frequent half of the two-tier scan (PRD §5.1). A stat-only walk of the media
/// tree — no file is opened — classifies each file against the catalog by <c>(path, size, mtime)</c>
/// and enqueues only the deltas for the (expensive) enrichment stage. Unchanged files do nothing,
/// which is what keeps a large library cheap. Single-flight (never two overlapping scans) and periodic
/// — the reliable baseline on network mounts where watchers are unreliable (PRD §5.4). A query never
/// triggers a scan; this is the only thing that writes discovery state.
/// </summary>
sealed class ScanService(
    MediaRepository repo,
    Channel<long> enrichQueue,
    IOptionsMonitor<LibraryOptions> options,
    ILogger<ScanService> log,
    IOptionsMonitor<ScanOptions> scanOptions) : BackgroundService
{
    readonly SemaphoreSlim singleFlight = new(1, 1);

    /// <summary>
    /// The library whose rows this scan manages (gh-#611): discovery's own
    /// <c>InsertDiscoveredAsync</c> writes rely on the schema's <c>library_id</c> default — library
    /// 'default', id 1 — so that is the one library whose out-of-root rows the quarantine below may
    /// judge. The safe library (id 2) manages its own rows and is never a scan concern.
    /// </summary>
    const long ScannedLibraryId = 1;

    // Consecutive-miss counters (SPEC F58, closes gitea-#223), keyed by path — in-memory only, zero
    // schema. Lives on the instance, not a static: a fresh ScanService (process restart) starts
    // every path at zero, so a flip can only be DEFERRED by a restart, never accelerated (F58.2).
    // A path enters this dictionary on its first miss and leaves it either on the next sighting
    // (reset) or on the tick that flips it unavailable (no further tracking needed once flipped).
    readonly Dictionary<string, int> consecutiveMisses = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CurrentScanInterval);
        do
        {
            try
            {
                await ScanOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Library scan failed");
            }

            // Re-read the interval fresh before the wait for the NEXT tick (SPEC F44.2, closes
            // gitea-#197) — a live edit to Library:ScanIntervalSeconds governs the delay to the next
            // tick without disturbing the tick that just completed. PeriodicTimer.Period has been
            // settable since .NET 8, so no timer teardown/rebuild is needed.
            timer.Period = CurrentScanInterval;
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>
    /// The scan interval as of right now — reads <see cref="IOptionsMonitor{T}.CurrentValue"/> fresh
    /// on every call rather than a boot-frozen snapshot (SPEC F44.2). Floored at 1 second so a live
    /// edit of 0 (or negative, if it ever slipped past <c>SettingValidator</c>) can never spin a
    /// tight timer loop.
    /// </summary>
    internal TimeSpan CurrentScanInterval =>
        TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.ScanIntervalSeconds));

    /// <summary>
    /// The consecutive-miss threshold as of right now — reads
    /// <see cref="IOptionsMonitor{T}.CurrentValue"/> fresh on every scan tick (SPEC F58.3), the same
    /// live shape as <see cref="CurrentScanInterval"/>. Floored at 1 so a live edit of 0 (or
    /// negative, if it ever slipped past <c>SettingValidator</c>) still reproduces the single-miss
    /// floor rather than never flipping anything.
    /// </summary>
    internal int CurrentMissThreshold => Math.Max(1, scanOptions.CurrentValue.MissThreshold);

    // internal for deterministic single-pass integration testing (no timer).
    internal async Task ScanOnceAsync(CancellationToken ct)
    {
        // Single-flight: if a scan is already running, skip this tick rather than overlap.
        if (!await singleFlight.WaitAsync(0, ct))
        {
            log.LogDebug("Scan already in progress; skipping this tick");
            return;
        }

        try
        {
            var known = new Dictionary<string, MediaFingerprint>();
            foreach (var fp in await repo.ListFingerprintsAsync(ct))
                known[fp.Path] = fp;

            var seen = new HashSet<string>();
            int discovered = 0, changed = 0;

            foreach (var file in EnumerateMedia())
            {
                ct.ThrowIfCancellationRequested();
                seen.Add(file.Path);

                // A sighting resets the grace counter (F58.1) — a no-op Remove when the path never
                // missed a tick, which is the common case.
                consecutiveMisses.Remove(file.Path);

                if (!known.TryGetValue(file.Path, out var existing))
                {
                    // New → insert as discovered, enqueue for enrichment.
                    var id = await repo.InsertDiscoveredAsync(file.Path, file.Format, file.SizeBytes, file.Mtime, ct);
                    await enrichQueue.Writer.WriteAsync(id, ct);
                    discovered++;
                }
                else if (existing.SizeBytes != file.SizeBytes || existing.Mtime != file.Mtime
                    || existing.State == "unavailable")
                {
                    // Changed → reset to discovered, enqueue for re-enrichment. An `unavailable`
                    // row whose path is sighted again takes this branch REGARDLESS of fingerprint
                    // (gh-#112): a file restored at its old path with size+mtime preserved
                    // (rsync -a, tar, mv back from a backup) is otherwise classified unchanged and
                    // skipped — stuck unavailable forever, resurrectable only by touch(1).
                    // Re-enrich rather than flip straight to ready: the fingerprint matching is
                    // strong evidence but not proof the AUDIO is identical, and one enrichment
                    // pass per resurrection is cheap (measurement children run niced, gh-#38).
                    await repo.MarkDiscoveredAsync(existing.Id, file.SizeBytes, file.Mtime, ct);
                    await enrichQueue.Writer.WriteAsync(existing.Id, ct);
                    changed++;
                }
                // Unchanged → skip (the common case; opens nothing, enqueues nothing).
            }

            // Missing → in the catalog (and not already unavailable) but gone from disk.
            // Scoped to rows under Library:MediaRoot only (F27.7): this scan walks MediaRoot alone, so
            // a row elsewhere (e.g. an authored row under /authored — inserted directly, never
            // discovered) was never a candidate to appear in `seen` and must not be treated as missing.
            // The scope lives here, in the diff, not in MarkUnavailableAsync itself — that write path
            // stays path-agnostic so any other caller (e.g. playout-time discovery, F5.6's other leg)
            // can still flip an authored row unavailable when it genuinely vanishes.
            var candidates = known.Values.Where(f => f.State != "unavailable" && IsUnderMediaRoot(f.Path) && !seen.Contains(f.Path));
            var missing = ApplyMissGrace(candidates);
            await repo.MarkUnavailableAsync(missing, ct);

            // Out-of-root ghosts (gh-#611) — scanned-library rows that lie outside MediaRoot AND
            // outside every exempt root (the F27.7 authored carve-out, now an explicit list). Such a
            // row can never appear in `seen` under ANY future tick of this configuration, so the F58
            // miss-grace above would defer it forever — it was discovered by a PREVIOUS root
            // configuration (the 2026-08-22 doubled-library incident: 9,112 host-path rows sat
            // `ready` for seven days, every pick of one dying silently at the engine). Quarantine
            // through the SAME grace counters (a one-scan root misconfiguration must not flip a
            // whole catalog) but WITHOUT the per-path first-miss log line — thirteen thousand of
            // those is a log bomb; the single summary below carries the same information. A
            // quarantined row resurrects through ordinary discovery (the gh-#112 branch above) the
            // moment its path is sighted under a root again.
            var exemptRoots = scanOptions.CurrentValue.QuarantineExemptRoots;
            var ghosts = known.Values
                .Where(f => f.State != "unavailable" && f.LibraryId == ScannedLibraryId
                    && !IsUnderMediaRoot(f.Path) && !IsUnderAnyRoot(f.Path, exemptRoots))
                .ToList();
            var quarantined = ApplyMissGrace(ghosts, logPerPath: false);
            await repo.MarkUnavailableAsync(quarantined, ct);
            if (quarantined.Count > 0)
                log.LogWarning(
                    "Scan: quarantined {Quarantined} catalog row(s) lying outside MediaRoot {MediaRoot} "
                    + "(prefixes: {Prefixes}) — unreachable under this root, likely discovered by a "
                    + "previous root configuration; they resurrect via normal discovery if the root "
                    + "moves back (gh-#611).",
                    quarantined.Count, options.CurrentValue.MediaRoot, SummarizePrefixes(ghosts));
            else if (ghosts.Count > 0)
                log.LogInformation(
                    "Scan: {Ghosts} catalog row(s) lie outside MediaRoot {MediaRoot} (prefixes: "
                    + "{Prefixes}) — deferring quarantine until the miss grace elapses (gh-#611, SPEC F58)",
                    ghosts.Count, options.CurrentValue.MediaRoot, SummarizePrefixes(ghosts));

            if (discovered > 0 || changed > 0 || missing.Count > 0)
                log.LogInformation("Scan: {Discovered} new, {Changed} changed, {Missing} missing",
                    discovered, changed, missing.Count);
        }
        finally
        {
            singleFlight.Release();
        }
    }

    /// <summary>
    /// Applies the F58 consecutive-miss grace to every candidate row absent from this tick's
    /// listing, returning the ids that just reached <see cref="CurrentMissThreshold"/> and should
    /// flip unavailable. Mutates <see cref="consecutiveMisses"/> in place: a below-threshold
    /// candidate's counter increments (and logs once, on its first miss, F58.4); a
    /// threshold-reaching candidate's counter is removed — it flips now, exactly as today, and is
    /// no longer tracked until it is rediscovered.
    /// </summary>
    List<long> ApplyMissGrace(IEnumerable<MediaFingerprint> candidates, bool logPerPath = true)
    {
        var missThreshold = CurrentMissThreshold;
        var missing = new List<long>();

        foreach (var f in candidates)
        {
            var misses = consecutiveMisses.TryGetValue(f.Path, out var priorMisses) ? priorMisses + 1 : 1;

            if (misses >= missThreshold)
            {
                missing.Add(f.Id);
                consecutiveMisses.Remove(f.Path);
            }
            else
            {
                consecutiveMisses[f.Path] = misses;
                // logPerPath=false is the gh-#611 ghost sweep: its candidates arrive thousands at a
                // time (a whole catalog discovered under a previous root), so the per-path line is
                // replaced by that sweep's own one-line summary per pass.
                if (logPerPath && misses == 1)
                    log.LogInformation(
                        "Scan: {Path} missing from listing (1 of {Threshold}) — deferring unavailable transition (SPEC F58)",
                        f.Path, missThreshold);
            }
        }

        return missing;
    }

    IEnumerable<MediaFile> EnumerateMedia()
    {
        // MediaRoot/SupportedExtensions are deployment topology, not operator-editable (F44.4) — read
        // once per scan pass here (renamed to avoid shadowing the EnumerationOptions local below).
        var cfg = options.CurrentValue;
        var enumOptions = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(cfg.MediaRoot, "*", enumOptions))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!cfg.SupportedExtensions.Contains(ext)) continue;

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (IOException)
            {
                continue;   // raced with a delete/rename; next scan picks it up
            }

            // Truncate mtime to whole seconds so it round-trips through timestamptz exactly and does
            // not spuriously re-trigger "changed" on sub-second precision differences.
            var mtime = TruncateToSeconds(info.LastWriteTimeUtc);
            yield return new MediaFile(path, ext.TrimStart('.'), info.Length, mtime);
        }
    }

    static DateTime TruncateToSeconds(DateTime t) =>
        new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    /// <summary>
    /// True when <paramref name="path"/> is <see cref="LibraryOptions.MediaRoot"/> itself or nested
    /// under it. Separator-aware so a sibling with the root as a string prefix (e.g. <c>/mediaX/file</c>
    /// against root <c>/media</c>) does not falsely match — a naive <c>StartsWith</c> on the raw
    /// strings would get this boundary wrong. Ordinal: this is a container filesystem path (Linux,
    /// case-sensitive), never culture text.
    /// </summary>
    bool IsUnderMediaRoot(string path) => IsUnder(path, options.CurrentValue.MediaRoot);

    /// <summary>True when <paramref name="path"/> lies under ANY of <paramref name="roots"/> —
    /// the gh-#611 exempt-root check, same separator-aware semantics as <see cref="IsUnder"/>.</summary>
    static bool IsUnderAnyRoot(string path, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
            if (IsUnder(path, root)) return true;
        return false;
    }

    static bool IsUnder(string path, string root)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(root);
        return path == trimmed || path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Up to three distinct top-of-tree prefixes (first two path segments) of the ghost rows, for
    /// the gh-#611 summary lines — enough to name "/home/dmills" without listing thirteen thousand
    /// paths.
    /// </summary>
    static string SummarizePrefixes(IEnumerable<MediaFingerprint> ghosts)
    {
        var prefixes = ghosts
            .Select(g => TopPrefix(g.Path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return prefixes.Count <= 3
            ? string.Join(", ", prefixes)
            : $"{string.Join(", ", prefixes.Take(3))}, +{prefixes.Count - 3} more";
    }

    static string TopPrefix(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => path,
            1 => Path.DirectorySeparatorChar + parts[0],
            _ => Path.DirectorySeparatorChar + parts[0] + Path.DirectorySeparatorChar + parts[1],
        };
    }
}
