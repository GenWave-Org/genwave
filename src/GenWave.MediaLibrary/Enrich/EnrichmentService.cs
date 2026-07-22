using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Mood;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.YearLookup;

namespace GenWave.MediaLibrary.Enrich;

/// <summary>
/// Enrichment: the expensive, throttled half of the two-tier scan (PRD §5.2). A bounded pool of
/// workers drains the delta queue discovery fills; each opens its file once, extracts everything, and
/// writes one atomic row update that flips the row to <c>ready</c>. Per-file failures are isolated
/// (the row becomes <c>failed</c>) and never crash a worker. Idempotent — safe to re-run after a crash.
///
/// Also runs a backfill loop for <c>ready</c> rows that pre-date cue analysis (SPEC F13 / T027):
/// picks up rows where <c>cue_analyzed_at IS NULL</c> and runs only <see cref="ICueAnalyzer"/> on them
/// (loudness is already present). Bounded to <see cref="CueDetectionOptions.BackfillBatchSize"/> rows
/// per iteration.
///
/// Additionally runs a backfill loop for <c>ready</c> rows that pre-date energy analysis (STORY-036 / E8):
/// picks up rows where <c>energy_analyzed_at IS NULL</c> and runs only <see cref="IEnergyAnalyzer"/>
/// on them, passing the row's existing cue points so energy uses the cue-trimmed windows. Loudness is
/// NOT re-run. Bounded to the same <see cref="CueDetectionOptions.BackfillBatchSize"/> per iteration.
///
/// Additionally runs a backfill loop for <c>ready</c> rows that pre-date BPM analysis (SPEC F46.3):
/// picks up rows where <c>bpm_analyzed_at IS NULL</c> and runs only <see cref="IBpmAnalyzer"/> on
/// them, passing the row's existing cue points so BPM is measured over the same cue-trimmed window
/// as energy. Loudness/cue are NOT re-run. Bounded to the same
/// <see cref="CueDetectionOptions.BackfillBatchSize"/> per iteration.
///
/// Additionally runs a backfill loop for <c>ready</c> rows missing a release year (SPEC F48.3,
/// F76.2): picks up rows where <c>year IS NULL AND year_lookup_missed_at IS NULL</c> and both artist
/// and title are non-blank, and runs <see cref="IYearLookup"/> on each — SEQUENTIALLY, one row
/// awaited at a time, never two calls in flight together. The MusicBrainz 1 req/s etiquette pacing
/// itself is NOT hand-rolled here (a hand-rolled per-loop delay only paces THIS loop, not every
/// caller): it lives in <c>MusicBrainzRateLimiter</c>, a process-wide gate <c>MusicBrainzYearLookup</c>
/// awaits immediately before every HTTP call (SPEC F76.1), so it holds regardless of which code path
/// drives the client. Skips entirely, with no claim query at all, when <c>Library:YearLookup:Enabled</c>
/// reads false (F48.5) — the live kill switch stops claiming before the very next tick, no api
/// restart. Bounded to the same <see cref="CueDetectionOptions.BackfillBatchSize"/> per tick.
///
/// Additionally recomputes the <c>energy</c> percentile column at the end of every backfill loop tick
/// (SPEC F80.1/F80.2, STORY-211): <c>energy</c> is the percentile rank of <c>integrated_lufs</c>
/// within the READY library, and is only meaningful relative to the whole population — so rather than
/// a per-row backfill claim, this checks once per tick whether any ready row's LUFS moved since the
/// last recompute (<see cref="MediaRepository.HasStaleEnergyPercentilesAsync"/>) and, if so, reranks
/// the entire ready library in one set-based UPDATE
/// (<see cref="MediaRepository.RecomputeEnergyPercentilesAsync"/>). A tick that touched no LUFS (e.g.
/// cue/BPM/year-lookup only) skips the recompute entirely.
///
/// Additionally runs a backfill loop for <c>ready</c> rows missing mood tags (SPEC F85.2-F85.4,
/// STORY-216, T72): picks up rows where <c>moods IS NULL AND mood_tag_missed_at IS NULL</c> and both
/// artist and title are non-blank, and runs <see cref="IMoodTagger"/> on each — SEQUENTIALLY, one row
/// awaited at a time, mirroring the year-lookup pass above. Gated FIRST by <see cref="ILlmBatchGate"/>
/// (SPEC F85.3): a Soft/Hard-degraded or unconfigured LLM skips the ENTIRE pass with one log line and
/// zero claim queries, never per-track noise — mood tagging must never compete with on-air
/// copywriting for a fenced model. Both dependencies are optional (default null); see the
/// constructor's own remarks for why. Bounded to the same
/// <see cref="CueDetectionOptions.BackfillBatchSize"/> per tick as every other backfill pass.
///
/// No separate scheduler or admin endpoint — all backfills fire as sub-tasks of this service.
/// </summary>
sealed class EnrichmentService(
    MediaRepository repo,
    Enricher enricher,
    Channel<long> enrichQueue,
    IOptionsMonitor<LibraryOptions> options,
    ILogger<EnrichmentService> log,
    ICueAnalyzer cueAnalyzer,
    IOptions<CueDetectionOptions> cueOptions,
    IEnergyAnalyzer energyAnalyzer,
    IBpmAnalyzer bpmAnalyzer,
    IYearLookup yearLookup,
    IOptionsMonitor<YearLookupOptions> yearLookupOptions,
    // Mood tagging (SPEC F85.2-F85.4, STORY-216, T72) — the newest backfill pass, and the first one
    // that is optional BY DESIGN rather than just absent in a test double: an unconfigured/degraded
    // LLM is a first-class supported state (F69), so "not wired at all" is simply the limiting case
    // of that same state. Optional (default null), unlike yearLookup above, purely so the ~10
    // existing Harness.cs factories that build an EnrichmentService for unrelated backfills (cue,
    // energy, bpm, year) keep compiling unchanged — mirrors the `events` parameter's own established
    // "no-op unless the host binds a real thing" idiom in this exact class, applied to a second
    // cross-cutting collaborator. A null moodTagger or null llmBatchGate makes the mood-tag backfill
    // pass a true no-op: no claim query, no log line (see BackfillMoodTagAsync).
    IMoodTagger? moodTagger = null,
    ILlmBatchGate? llmBatchGate = null,
    IStationEventSink? events = null) : BackgroundService
{
    // EnrichmentCompleted publish seam (gitea-#246); no-op unless the host binds a real sink.
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    // Live worker headcount (SPEC F44.2, closes gitea-#197) — NOT a boot-frozen `readonly int` snapshot of
    // Library:EnrichmentConcurrency. ReconcileWorkerPool grows/shrinks this toward the CURRENT
    // configured value on the same cadence as the backfill loop below (no second timer). Growing
    // spawns workers immediately; shrinking is cooperative — see WorkerAsync — so an item already
    // being enriched always finishes under the value that was in effect when it started.
    int activeWorkerCount;

    // Tracked worker-task set (SPEC F59, closes gitea-#222). A plain List<Task> guarded by workerTasksLock —
    // NOT a ConcurrentBag<Task>, which has no Remove and so accumulated every completed/retired worker
    // forever on a long-lived process that repeatedly raises concurrency after lowering it.
    // ReconcileWorkerPool prunes completed entries before growing, so the tracked count stays bounded
    // by the current target concurrency plus whatever retirees haven't finished unwinding yet — it
    // never grows monotonically across grow/shrink cycles. ReconcileWorkerPool itself is only ever
    // invoked sequentially (once at startup, then once per backfill-loop tick), so the lock exists to
    // serialize its prune-then-add against the shutdown read in ExecuteAsync, not against itself.
    readonly object workerTasksLock = new();
    readonly List<Task> workerTasks = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Start the initial worker set FIRST so the (bounded) channel is already being drained before
        // recovery refills it below — otherwise a large recovery batch could block on a full channel
        // with no reader yet, deadlocking startup.
        ReconcileWorkerPool(ct);

        // Recovery: the in-memory channel does not survive a restart, and discovery only enqueues disk
        // deltas — so any row left in 'discovered' from a previous run (a backlog interrupted by a crash
        // or redeploy) would otherwise be orphaned forever. Re-drive it from the durable work queue.
        // Startup-only (not periodic), so rows mid-enrichment in a healthy process are never double-queued;
        // and enrichment is idempotent, so a row also freshly enqueued by a concurrent first scan just
        // gets harmlessly re-enriched.
        await RequeuePendingAsync(ct);

        // Backfill loop: runs as a fire-and-forget sub-task alongside the enrichment workers, and
        // also reconciles the worker pool on each of its ticks (SPEC F44.2). No new BackgroundService,
        // no admin endpoint — same control plane, same process lifetime.
        var backfillTask = Task.Run(() => RunBackfillLoopAsync(ct), ct);

        // Block until shutdown: workers now come and go (ReconcileWorkerPool), so there is no fixed
        // array to await here — park on cancellation instead, then unwind everything spawned so far.
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }

        await backfillTask;

        Task[] tasksSnapshot;
        lock (workerTasksLock)
            tasksSnapshot = workerTasks.ToArray();
        await Task.WhenAll(tasksSnapshot);
    }

    /// <summary>
    /// Grows the live worker pool toward <see cref="LibraryOptions.EnrichmentConcurrency"/>'s CURRENT
    /// value (floored at 1). Never removes a worker directly — a shrink is enforced cooperatively
    /// inside <see cref="WorkerAsync"/>, where a worker retires itself between items once the pool is
    /// over budget, so anything already in flight always completes under the value it started with.
    /// internal for deterministic testing (no timer) — mirrors <c>ScanOnceAsync</c>'s own convention.
    /// </summary>
    internal void ReconcileWorkerPool(CancellationToken ct)
    {
        var desired = Math.Max(1, options.CurrentValue.EnrichmentConcurrency);
        lock (workerTasksLock)
        {
            // Prune first (F59.1): a worker that already finished/retired since the last reconcile
            // is dead weight — dropping it here, before any growth below, is what keeps the tracked
            // set bounded by desired + in-flight retirees instead of growing across every subsequent
            // grow/shrink cycle for the life of the process.
            workerTasks.RemoveAll(t => t.IsCompleted);

            while (Volatile.Read(ref activeWorkerCount) < desired)
            {
                // gh-#6: a completed-AND-empty enrich channel makes WaitToReadAsync return an
                // already-completed `false` synchronously — a newly-spawned WorkerAsync would run
                // to completion (and decrement activeWorkerCount right back down) before this loop's
                // next iteration even runs, so the count above never rises and this spins hot forever
                // holding workerTasksLock. Reader.Completion mirrors that exact condition (it only
                // completes once the writer is done AND every item has been drained) without needing
                // to spawn a worker just to find out — bail instead of spinning. Not reachable today
                // (no caller ever calls enrichQueue.Writer.Complete()), but growing against a reader
                // that will never again yield an item is futile regardless of how it got that way.
                if (enrichQueue.Reader.Completion.IsCompleted)
                {
                    log.LogWarning(
                        "Enrich queue reader is completed; stopping worker pool growth at {Count} (desired {Desired})",
                        Volatile.Read(ref activeWorkerCount), desired);
                    break;
                }

                Interlocked.Increment(ref activeWorkerCount);
                workerTasks.Add(WorkerAsync(ct));
            }
        }
    }

    /// <summary>Current live worker headcount — internal for deterministic testing.</summary>
    internal int ActiveWorkerCount => Volatile.Read(ref activeWorkerCount);

    /// <summary>Current size of the tracked worker-task set — internal for deterministic testing (SPEC F59.1).</summary>
    internal int TrackedWorkerTaskCount
    {
        get
        {
            lock (workerTasksLock)
                return workerTasks.Count;
        }
    }

    // internal for integration testing — reproduce the restart-orphan case directly.
    internal async Task RequeuePendingAsync(CancellationToken ct)
    {
        try
        {
            var pending = await repo.ListPendingEnrichmentAsync(ct);
            if (pending.Count == 0) return;

            log.LogInformation("Recovering {Count} pending enrichment row(s) from a previous run", pending.Count);
            foreach (var id in pending)
                await enrichQueue.Writer.WriteAsync(id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // A failed recovery query must not take the service down — workers still drain live scan
            // deltas; the next restart retries recovery.
            log.LogError(ex, "Failed to recover pending enrichment on startup");
        }
    }

    async Task WorkerAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                // Cooperative shrink (SPEC F44.2): if the live pool now exceeds the CURRENT
                // Library:EnrichmentConcurrency, this worker retires — checked only BETWEEN
                // items, never mid-EnrichOneAsync, so anything already in flight always finishes
                // under the value it started with. Overshooting the retirement (several workers
                // seeing the same excess at once) self-corrects: ReconcileWorkerPool tops the
                // pool back up to the desired count on its next tick if that ever happens.
                if (Volatile.Read(ref activeWorkerCount) > Math.Max(1, options.CurrentValue.EnrichmentConcurrency))
                    return;

                if (!await enrichQueue.Reader.WaitToReadAsync(ct))
                    return;   // channel completed

                if (enrichQueue.Reader.TryRead(out var id))
                    await EnrichOneAsync(id, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        finally
        {
            Interlocked.Decrement(ref activeWorkerCount);
        }
    }

    // internal for integration testing (drive one file through enrich/write without the queue).
    internal async Task EnrichOneAsync(long id, CancellationToken ct)
    {
        var path = await repo.GetPathAsync(id, ct);
        if (path is null) return;   // the row vanished between discovery and enrichment

        try
        {
            var result = await enricher.EnrichAsync(path, ct);
            await repo.WriteEnrichmentAsync(id, result, ct);
            events.Publish(new EnrichmentCompleted(id, Succeeded: true));   // row flipped ready (gitea-#246)
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // let the worker unwind on shutdown
        }
        catch (Exception ex)
        {
            // Per-file failure (corrupt file, unreadable tags) is isolated — mark failed, keep going.
            log.LogWarning(ex, "Enrichment failed for {Path}; marking failed", path);
            try
            {
                await repo.MarkFailedAsync(id, ct);
                events.Publish(new EnrichmentCompleted(id, Succeeded: false));   // row flipped failed (gitea-#246)
            }
            catch (Exception markEx)
            {
                log.LogError(markEx, "Could not mark media {Id} failed", id);
            }
        }
    }

    async Task RunBackfillLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await BackfillCueAsync(ct);
                await BackfillEnergyAsync(ct);
                await BackfillBpmAsync(ct);
                await BackfillYearLookupAsync(ct);
                await BackfillMoodTagAsync(ct);
                await RecomputeEnergyPercentileAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Backfill loop iteration failed; will retry after interval");
            }

            // Reconcile the enrichment worker pool toward the LIVE Library:EnrichmentConcurrency
            // value on the same cadence as the interval delay below (SPEC F44.2) — no second timer.
            ReconcileWorkerPool(ct);

            try
            {
                // Read fresh per iteration (SPEC F44.2) — never a boot-frozen snapshot — so a live
                // edit to Library:ScanIntervalSeconds shortens/lengthens the NEXT wait, never the
                // one already in progress. Floored at 1s, mirroring ScanService's own clamp.
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.ScanIntervalSeconds)), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // internal for integration testing — drive the backfill pass directly.
    internal async Task BackfillCueAsync(CancellationToken ct)
    {
        var batch = await repo.ListBackfillCueAsync(cueOptions.Value.BackfillBatchSize, ct);
        if (batch.Count == 0) return;
        log.LogInformation("Backfilling cue points for {Count} ready rows", batch.Count);
        foreach (var id in batch)
        {
            var path = await repo.GetPathAsync(id, ct);
            if (path is null) continue;
            CuePoints? cue = null;
            try
            {
                cue = await cueAnalyzer.AnalyzeAsync(path, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Backfill cue analysis failed for {Path}", path);
            }
            await repo.WriteCueBackfillAsync(id, cue, ct);
        }
    }

    // internal for integration testing — drive the energy backfill pass directly.
    internal async Task BackfillEnergyAsync(CancellationToken ct)
    {
        var batch = await repo.ListEnergyClaimsAsync(cueOptions.Value.BackfillBatchSize, ct);
        if (batch.Count == 0) return;
        log.LogInformation("Backfilling energy for {Count} ready rows", batch.Count);
        foreach (var row in batch)
        {
            EnergyPoints? energy = null;
            try
            {
                // Pass the row's existing cue points so energy is measured over the cue-trimmed
                // windows. Loudness is NOT re-run — it already succeeded during first-pass enrichment.
                energy = await energyAnalyzer.AnalyzeAsync(row.Path, row.CueInSec, row.CueOutSec, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Backfill energy analysis failed for {Path}", row.Path);
            }
            await repo.WriteEnergyClaimAsync(row.Id, energy, ct);
        }
    }

    // internal for integration testing — drive the BPM backfill pass directly.
    internal async Task BackfillBpmAsync(CancellationToken ct)
    {
        var batch = await repo.ListBpmClaimsAsync(cueOptions.Value.BackfillBatchSize, ct);
        if (batch.Count == 0) return;
        log.LogInformation("Backfilling BPM for {Count} ready rows", batch.Count);
        foreach (var row in batch)
        {
            double? bpm = null;
            try
            {
                // Pass the row's existing cue points so BPM is measured over the cue-trimmed
                // windows, same as energy. Loudness/cue are NOT re-run — they already succeeded
                // during first-pass enrichment.
                bpm = await bpmAnalyzer.AnalyzeAsync(row.Path, row.CueInSec, row.CueOutSec, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Backfill BPM analysis failed for {Path}", row.Path);
            }
            await repo.WriteBpmClaimAsync(row.Id, bpm, ct);
        }
    }

    // internal for integration testing — drive the year-lookup backfill pass directly.
    internal async Task BackfillYearLookupAsync(CancellationToken ct)
    {
        // Kill switch (SPEC F48.5): false stops claiming before the very next tick — no claim query
        // is even issued, read fresh per tick (never a boot-frozen snapshot).
        if (!yearLookupOptions.CurrentValue.Enabled)
            return;

        var batch = await repo.ListYearLookupClaimsAsync(cueOptions.Value.BackfillBatchSize, ct);
        if (batch.Count == 0) return;
        log.LogInformation("Backfilling release year for {Count} ready rows", batch.Count);

        // Aggregated across the whole batch so an unreachable endpoint WARNs once per tick, not
        // once per row (SPEC F48.5) — IYearLookupDiagnostics is the out-of-band signal; an
        // implementation that doesn't carry it (e.g. a test double proving only "no match") is
        // simply never treated as failing.
        var anyCallFailed = false;

        foreach (var row in batch)
        {
            int? year = null;
            var rowCallFailed = false;
            try
            {
                year = await yearLookup.TryLookupAsync(row.Artist ?? string.Empty, row.Title ?? string.Empty, row.Album, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The Core contract promises IYearLookup never throws past its boundary (F48.1); this
                // guard is defense-in-depth only, mirroring BackfillBpmAsync/BackfillEnergyAsync — a
                // misbehaving implementation must not take the whole backfill loop down.
                log.LogWarning(ex, "Backfill year lookup failed for {Artist} - {Title}", row.Artist, row.Title);
                rowCallFailed = true;
            }

            if (yearLookup is IYearLookupDiagnostics diagnostics && diagnostics.LastCallFailed)
                rowCallFailed = true;

            if (rowCallFailed)
                anyCallFailed = true;

            // year_lookup_at is stamped unconditionally (attempted-at telemetry); the
            // year_lookup_missed_at re-claim gate is stamped ONLY for a genuine miss — never for a
            // failed round trip, which stays eligible and is retried next tick (SPEC F76.2).
            await repo.WriteYearLookupResultAsync(row.Id, year, rowCallFailed, ct);
        }

        if (anyCallFailed)
            log.LogWarning(
                "MusicBrainz year lookup appears unreachable this tick; {Count} row(s) attempted", batch.Count);
    }

    /// <summary>
    /// The mood-tag backfill pass (SPEC F85.2-F85.4, STORY-216, T72) — internal for integration
    /// testing, drives the pass directly.
    /// <para>
    /// Not wired (either dependency null, the common case for a test double built by an unrelated
    /// Harness factory) is a silent no-op: no gate evaluation, no claim query, no log line.
    /// </para>
    /// <para>
    /// When wired, <see cref="ILlmBatchGate.Evaluate"/> runs FIRST, before any claim query (SPEC
    /// F85.3) — a Soft/Hard-degraded or unconfigured LLM logs exactly ONE line for the whole tick and
    /// returns; mood tagging must never compete with on-air copywriting for a fenced model. Only once
    /// the gate allows does this issue <see cref="MediaRepository.ListMoodTagClaimsAsync"/> and, for
    /// each claimed row, one sequential <see cref="IMoodTagger.TagAsync"/> call — mirrors
    /// <see cref="BackfillYearLookupAsync"/>'s own one-row-at-a-time pacing and failed-vs-miss split
    /// (<see cref="IMoodTaggerDiagnostics"/> mirrors <see cref="IYearLookupDiagnostics"/> exactly).
    /// </para>
    /// <para>
    /// A non-empty result is written through <see cref="MediaRepository.WriteMoodsAsync"/> — T58's own
    /// validating write path is the ONE backstop for what actually lands in the <c>moods</c> column;
    /// this method never re-validates a mood set itself. Every row, regardless of outcome, gets
    /// exactly one <see cref="MediaRepository.StampMoodTagAttemptAsync"/> call (the F76 etiquette
    /// stamp): a failed round trip stamps attempted-at only (retried next tick); a genuine miss (zero
    /// survivors) additionally stamps the re-claim gate; a written mood set needs no miss stamp at all
    /// since <c>moods</c> is no longer null.
    /// </para>
    /// </summary>
    internal async Task BackfillMoodTagAsync(CancellationToken ct)
    {
        if (moodTagger is null || llmBatchGate is null) return;

        var decision = llmBatchGate.Evaluate();
        if (!decision.Allowed)
        {
            log.LogInformation("Mood tagging batch skipped this tick: {Reason}", decision.Reason);
            return;
        }

        var batch = await repo.ListMoodTagClaimsAsync(cueOptions.Value.BackfillBatchSize, ct);
        if (batch.Count == 0) return;
        log.LogInformation("Tagging moods for {Count} ready rows", batch.Count);

        var anyCallFailed = false;

        foreach (var row in batch)
        {
            IReadOnlyList<string> moods = [];
            try
            {
                moods = await moodTagger.TagAsync(row.Artist, row.Title, row.Genre, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The Core contract promises IMoodTagger never throws past its boundary (F85.4); this
                // guard is defense-in-depth only, mirroring BackfillYearLookupAsync's own catch-all.
                log.LogWarning(ex, "Mood tagging failed for {Artist} - {Title}", row.Artist, row.Title);
            }

            var rowCallFailed = moodTagger is IMoodTaggerDiagnostics diagnostics && diagnostics.LastCallFailed;
            if (rowCallFailed)
                anyCallFailed = true;

            if (!rowCallFailed && moods.Count > 0)
            {
                var result = await repo.WriteMoodsAsync(row.Id, moods, ct);
                if (result != MoodWriteResult.Written)
                {
                    // Defense-in-depth only — MoodTagParser already filters to the vocabulary and
                    // caps at MaxMoodsPerTrack, so WriteMoodsAsync should never actually reject this
                    // in practice. Treated as a failure, not a miss: don't stamp the row away
                    // permanently for what would be this pipeline's own bug, not the model's.
                    log.LogWarning(
                        "Mood write rejected ({Result}) for media {Id}; will retry", result, row.Id);
                    rowCallFailed = true;
                    anyCallFailed = true;
                }
            }

            // mood_tagged_at is stamped unconditionally (attempted-at telemetry); the
            // mood_tag_missed_at re-claim gate is stamped ONLY for a genuine miss — never for a
            // failed round trip, which stays eligible and is retried next tick (SPEC F85.4, F76.2).
            var missed = !rowCallFailed && moods.Count == 0;
            await repo.StampMoodTagAttemptAsync(row.Id, missed, ct);
        }

        if (anyCallFailed)
            log.LogWarning(
                "Mood tagger LLM appears unreachable this tick; {Count} row(s) attempted", batch.Count);
    }

    /// <summary>
    /// The F80.2 piggyback: recomputes the <c>energy</c> percentile column across the whole ready
    /// library, but ONLY when at least one ready row's LUFS was added or changed since the last
    /// recompute (<see cref="MediaRepository.HasStaleEnergyPercentilesAsync"/>) — a tick that touched
    /// no LUFS (e.g. cue/BPM/year-lookup only) issues no recompute UPDATE at all. Unlike the other
    /// backfill passes above, this is never claim-batched by row count: a percentile is meaningless
    /// for less than the full ready population, so a stale hit always reranks every ready row in one
    /// set-based UPDATE (<see cref="MediaRepository.RecomputeEnergyPercentilesAsync"/>).
    /// internal for integration testing — drive the recompute pass directly.
    /// </summary>
    internal async Task RecomputeEnergyPercentileAsync(CancellationToken ct)
    {
        if (!await repo.HasStaleEnergyPercentilesAsync(ct)) return;
        log.LogInformation("Recomputing energy percentiles across the ready library (SPEC F80.2)");
        await repo.RecomputeEnergyPercentilesAsync(ct);
    }
}
