using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Scan;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// <see cref="IFileActionExecutor"/>'s one implementation (SPEC F154.4, F154.6-F154.8; STORY-379;
/// PLAN T380, gh-#529; T380 review B1-B6, N1-N10, R2-1/R2-2/R2-3) — Dapper-FREE (L2: every SQL
/// statement lives in <see cref="FileActionRepository"/>). Steps, in order, ALL inside the shared
/// <see cref="IScanGate"/>:
/// <list type="number">
/// <item>re-read the row's binding and re-check it against the plan (TOCTOU, SPEC F154.5) —
/// <see cref="FileActionOutcomeKind.Conflict"/> on a mismatch;</item>
/// <item>re-assert BOTH endpoints are still under the canonical <c>MediaRoot</c> (T380 review N2,
/// defence in depth — a plan token is HMAC-bound to its own payload, not to the CURRENT root
/// config);</item>
/// <item>re-probe the computed target for rename/move (F154.4 never-overwrite), and — move only —
/// confirm source and target share a filesystem device (T380 review B4, R2-3);</item>
/// <item>the ONE filesystem COMMIT — <see cref="File.Move(string, string, bool)"/> without overwrite
/// for rename/move, or a copy-retag-swap for retag (T380 review B1/B5: the ORIGINAL is copied to a
/// same-directory, per-attempt-unique <c>.gwtmp</c> file, TagLibSharp writes only the COPY, then two
/// same-directory (POSIX-atomic) renames swap it in behind a same-directory, per-attempt-unique
/// <c>.gwbak</c> backup — a mid-retag crash therefore damages only the tmp, never the live file);
/// </item>
/// <item>re-stat the result with <see cref="Scan.ScanMtime"/>'s own truncation rule — the IDENTICAL
/// rule the next scan tick judges "unchanged" by (F154.6);</item>
/// <item>one transaction: the xmin-guarded row update plus the audit insert
/// (<see cref="FileActionRepository.RelocateAsync"/>);</item>
/// <item>on any failure PAST the commit point (re-stat, or the database write), revert the filesystem
/// change and write a <c>reverted</c> audit row — <see cref="FileActionOutcomeKind.Reverted"/>, now
/// honest for retag too (the pre-retag original is restored byte-for-byte from its own backup), or
/// <see cref="FileActionOutcomeKind.Failed"/> if the revert itself fails too
/// (<c>detail.revert = false</c>, WARN with the media id only).</item>
/// </list>
///
/// <para>
/// <b>Cancellation (T380 review B3, R2-1):</b> the caller's own <see cref="CancellationToken"/> is
/// honoured only through the gate wait and the binding re-read — both entirely BEFORE any filesystem
/// write. The independent, 30-second-bounded post-commit token is constructed ONLY AFTER the gate
/// wait has already completed (R2-1: constructing it any earlier lets a long gate wait eat into — or
/// entirely exhaust — its own budget before the post-commit phase even begins, which at the default
/// 30-second <c>GateTimeoutSeconds</c> could hand a Busy audit write an ALREADY-cancelled token).
/// Every repository call from the gate result onward (the busy/conflict/refused audits, the relocate
/// transaction, and any revert) runs on this fresh token, never the caller's — once the commit point
/// is reached, the caller cancelling has no effect: a half-done "file moved but the row was never
/// updated because the request was cancelled" state must never happen.
/// </para>
///
/// <para>
/// <b>No exception with a path ever escapes <see cref="ExecuteAsync"/></b> (T380 review B2): the
/// entire body is wrapped, and an exception type this class does not explicitly catalog maps to
/// <see cref="FileActionOutcomeKind.Failed"/> with a WARN naming the media id and verb only.
/// </para>
///
/// Every audit row's <c>detail</c> jsonb is <c>{}</c> except <c>refused</c>
/// (<c>{"rule": "..."}</c>) and <c>failed</c>/<c>reverted</c> (<c>{"reason": "io"|"db"[, "revert":
/// false]}</c>) — no path ever reaches a log template or an audit row (F154.3's own "path never
/// echoed" posture, extended here to the write side).
/// </summary>
sealed class FileActionExecutor(
    IScanGate gate,
    IFileSystemProbe probe,
    FileActionRepository repository,
    IOptionsMonitor<LibraryOptions> libraryOptions,
    IOptionsMonitor<GardenerOptions> gardenerOptions,
    ILogger<FileActionExecutor> logger,
    TimeSpan? postCommitBudget = null) : IFileActionExecutor
{
    const string EmptyDetail = "{}";

    /// <summary>The FS-write-commit's own post-commit bound (T380 review B3) — independent of the
    /// caller's token; long enough to ride out an ordinary DB hiccup without leaving a filesystem
    /// change unrecorded for good. Constructed fresh AFTER the gate wait (R2-1) — see this class's
    /// own remarks. Injectable via the trailing constructor parameter (T380 review round 3, the
    /// <c>Step3bFailureDetail</c> test-seam precedent) — production leaves it unset and gets the
    /// 30-second default; a fact wanting a deterministic, fast-failing budget passes a smaller one.
    /// DI never sees this parameter as a service (no <see cref="TimeSpan"/> is registered), so
    /// <c>Microsoft.Extensions.DependencyInjection</c> falls through to the default automatically.
    /// </summary>
    static readonly TimeSpan DefaultPostCommitBudget = TimeSpan.FromSeconds(30);

    readonly TimeSpan postCommitBudget = postCommitBudget ?? DefaultPostCommitBudget;

    public async Task<FileActionOutcome> ExecuteAsync(FileActionPlan plan, string planToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(planToken);

        try
        {
            var timeoutSeconds = Math.Clamp(gardenerOptions.CurrentValue.FileActions.GateTimeoutSeconds, 1, 300);
            var lease = await gate.EnterAsync(TimeSpan.FromSeconds(timeoutSeconds), ct);

            // R2-1: the post-commit token is built HERE — after the gate wait has already run its
            // course, however long that took — never before it. A stale, partially- or
            // fully-consumed budget must never reach the busy audit write below, let alone the
            // post-commit phase inside the gate.
            using var postCommitCts = new CancellationTokenSource(postCommitBudget);
            var postCommitCt = postCommitCts.Token;

            if (lease is null)
            {
                await repository.AuditAsync(BuildAudit(plan, planToken, FileActionOutcomeKind.Busy, EmptyDetail), postCommitCt);
                return new FileActionOutcome(FileActionOutcomeKind.Busy, null);
            }

            try
            {
                return await ExecuteUnderGateAsync(plan, planToken, ct, postCommitCt);
            }
            finally
            {
                lease.Dispose();
            }
        }
        catch (Exception)
        {
            // T380 review B2: the generic safety net — an exception type not explicitly cataloged
            // below (including the caller's own cancellation observed before the FS op even started).
            // Never the exception object itself (no path ever reaches a log template).
            logger.LogWarning("File action {Verb} failed unexpectedly for media {MediaId}", plan.Verb, plan.MediaId);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }
    }

    async Task<FileActionOutcome> ExecuteUnderGateAsync(
        FileActionPlan plan, string planToken, CancellationToken ct, CancellationToken postCommitCt)
    {
        var binding = await repository.ReadBindingAsync(plan.MediaId, ct);
        if (binding is null || !PlanBinding.Matches(plan, binding.Xmin, binding.Path))
        {
            await repository.AuditAsync(BuildAudit(plan, planToken, FileActionOutcomeKind.Conflict, EmptyDetail), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Conflict, null);
        }

        // T380 review N2: defence in depth, re-asserted right here inside the gate, immediately
        // before any write — a plan token is HMAC-bound to its OWN payload, never to the current
        // MediaRoot configuration, so a stale token surviving a MediaRoot move (or a forged/replayed
        // one in a misconfigured deploy) must never let a write land outside the root the jail
        // promises.
        if (!IsUnderCanonicalRoot(plan.From) || !IsUnderCanonicalRoot(plan.To))
        {
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Refused, DetailForRule(FileActionRule.OutsideRoot)), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Refused, FileActionRule.OutsideRoot);
        }

        if (plan.Verb is FileActionVerb.Rename or FileActionVerb.Move)
        {
            var refusalRule = CheckTarget(plan);
            if (refusalRule is { } rule)
            {
                await repository.AuditAsync(BuildAudit(plan, planToken, FileActionOutcomeKind.Refused, DetailForRule(rule)), postCommitCt);
                return new FileActionOutcome(FileActionOutcomeKind.Refused, rule);
            }
        }

        return plan.Verb == FileActionVerb.Retag
            ? await ExecuteRetagAsync(plan, planToken, postCommitCt)
            : await ExecuteMoveOrRenameAsync(plan, planToken, postCommitCt);
    }

    /// <summary>N2's own root re-check — separator-aware containment against
    /// <c>Library:MediaRoot</c> as configured RIGHT NOW, mirroring <c>ScanService.IsUnder</c>'s exact
    /// semantics (a naive <c>StartsWith</c> on the raw strings would get a sibling-prefix boundary
    /// wrong).</summary>
    bool IsUnderCanonicalRoot(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryOptions.CurrentValue.MediaRoot));
        var candidate = Path.GetFullPath(path);
        return candidate == root || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>SPEC F154.4's never-overwrite check, re-probed right before the write (the plan may
    /// be up to 10 minutes old — SPEC F154.5 — so a target that appeared since dry-run must be
    /// caught here, not trusted from the plan).</summary>
    FileActionRule? CheckTarget(FileActionPlan plan)
    {
        if (probe.Kind(plan.To) != FileSystemEntryKind.Missing)
            return FileActionRule.TargetExists;

        if (plan.Verb == FileActionVerb.Move)
        {
            var targetDir = Path.GetDirectoryName(plan.To);
            if (targetDir is null || probe.Kind(targetDir) != FileSystemEntryKind.Directory)
                return FileActionRule.TargetNotADirectory;

            // T380 review B4/R2-3: same-device check, BEFORE any move — a cross-device File.Move is
            // not an atomic rename.
            if (!SameDevice(plan.From, targetDir))
                return FileActionRule.CrossDevice;
        }

        return null;
    }

    /// <summary>
    /// T380 review R2-3's own ruling: off Linux, an INCONCLUSIVE stat SKIPS the check (proceeds) —
    /// there is no real cross-device risk this codebase's own dev-workstation posture needs to
    /// enforce, and <see cref="IFileSystemProbe.TryGetDeviceId"/> always reports "unknown" there
    /// anyway. On Linux (the appliance's own deploy target, ARCHITECTURE.md), an inconclusive stat
    /// REFUSES instead — a stat that SHOULD have worked and didn't is itself a signal something is
    /// wrong, never a reason to silently risk a non-atomic move. Both "proved different" and "could
    /// not prove same" report the SAME <see cref="FileActionRule.CrossDevice"/> — see that member's
    /// own remarks for why one rule covers both.
    /// </summary>
    bool SameDevice(string sourcePath, string targetDir)
    {
        var sourceKnown = probe.TryGetDeviceId(sourcePath, out var sourceDevice);
        var targetKnown = probe.TryGetDeviceId(targetDir, out var targetDevice);

        if (sourceKnown && targetKnown)
            return sourceDevice == targetDevice;

        return !OperatingSystem.IsLinux();
    }

    async Task<FileActionOutcome> ExecuteMoveOrRenameAsync(FileActionPlan plan, string planToken, CancellationToken postCommitCt)
    {
        try
        {
            File.Move(plan.From, plan.To, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, DetailForFailure("io")), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        long sizeBytes;
        DateTime mtime;
        try
        {
            var info = new FileInfo(plan.To);
            sizeBytes = info.Length;
            mtime = ScanMtime.TruncateToSeconds(info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            // T380 review B2: past the commit point — a re-stat failure is a STRAND, not a plain
            // Failed(io); it routes through the exact same revert path a DB failure would.
            return await RevertMoveOrRenameAsync(plan, planToken, plan.To, "io", postCommitCt);
        }

        var doneAudit = BuildAudit(plan, planToken, FileActionOutcomeKind.Done, EmptyDetail);
        var affected = await repository.RelocateAsync(plan.MediaId, plan.Xmin, plan.To, sizeBytes, mtime, doneAudit, postCommitCt);

        return affected > 0
            ? new FileActionOutcome(FileActionOutcomeKind.Done, null)
            : await RevertMoveOrRenameAsync(plan, planToken, plan.To, "db", postCommitCt);
    }

    /// <summary>Rename/move's own revert: the file currently sits at <c>revertTo</c> (
    /// <c>plan.To</c>) and must move back to <c>plan.From</c>, which is expected to be EMPTY (nothing
    /// was ever left there) — <c>overwrite: false</c>, unlike retag's own bak-restore.</summary>
    async Task<FileActionOutcome> RevertMoveOrRenameAsync(
        FileActionPlan plan, string planToken, string revertTo, string reason, CancellationToken postCommitCt)
    {
        try
        {
            File.Move(revertTo, plan.From, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "File action revert failed for media {MediaId} verb {Verb}; the file was left at its new location",
                plan.MediaId, plan.Verb);
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, DetailForFailure(reason, revertSucceeded: false)),
                postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        await repository.AuditAsync(BuildAudit(plan, planToken, FileActionOutcomeKind.Reverted, DetailForFailure(reason)), postCommitCt);
        return new FileActionOutcome(FileActionOutcomeKind.Reverted, null);
    }

    /// <summary>
    /// The T380 review B1/B5 copy-retag-swap shape. <paramref name="plan"/>'s <c>From</c>/<c>To</c>
    /// are identical for retag (its destination is its source) — the PATH never changes; only the
    /// underlying inode does, which the scan does not care about (it is path-keyed).
    /// </summary>
    async Task<FileActionOutcome> ExecuteRetagAsync(FileActionPlan plan, string planToken, CancellationToken postCommitCt)
    {
        var directory = Path.GetDirectoryName(plan.From)
            ?? throw new InvalidOperationException("a retag subject always has a parent directory (the jail already proved it).");
        var fileName = Path.GetFileName(plan.From);

        // T380 review R2-2: a pre-existing *.gwbak sibling for THIS file — a leftover from a prior
        // attempt that never cleaned up after itself (a revert-failure by design, a failed delete,
        // or a crash mid-attempt) — refuses outright rather than silently colliding with or ignoring
        // it, so a stuck file is diagnosable instead of an every-future-retag-fails-forever trap.
        if (HasLeftoverBackup(directory, fileName))
        {
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Refused, DetailForRule(FileActionRule.LeftoverBackup)), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Refused, FileActionRule.LeftoverBackup);
        }

        // T380 review R2-2: a unique-per-attempt suffix — a deterministic name would let ANY leftover
        // (impossible to fully rule out: the check above is best-effort, not a lock) collide with a
        // later attempt's own tmp/bak files. Still outside Library:SupportedExtensions either way —
        // Path.GetExtension only ever looks at the LAST dot-segment, so "x.mp3.a1b2c3d4.gwbak" is
        // still extension ".gwbak" regardless of what precedes it, and the scan ignores it exactly
        // like it always ignored the old deterministic name.
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var tmpPath = Path.Combine(directory, $"{fileName}.{suffix}.gwtmp");
        var bakPath = Path.Combine(directory, $"{fileName}.{suffix}.gwbak");

        long preSize;
        DateTime preRawMtime;
        try
        {
            var preInfo = new FileInfo(plan.From);
            preSize = preInfo.Length;
            preRawMtime = preInfo.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, DetailForFailure("io")), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        // Step 1: copy the original to a same-directory tmp — the ORIGINAL is untouched by
        // everything that follows up to the commit point below.
        try
        {
            File.Copy(plan.From, tmpPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, DetailForFailure("io")), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        // Step 2: retag the COPY. TagLibSharp opens/saves ONLY tmpPath — a mid-Save crash here
        // damages the tmp file alone; the original at plan.From is still exactly what it was before
        // this attempt started. A structurally safe design (T380 review B1): no separate "simulate a
        // mid-retag crash" fact is needed, because no code path between this Save and the commit
        // point below can leave the ORIGINAL half-written.
        try
        {
            // TagLib sniffs the container format from the PATH's own extension when no mimetype is
            // given (TagLib.File.Create(string)'s own implementation) — tmpPath's real extension is
            // ".gwtmp", not the media container, so the mimetype is built explicitly from plan.From's
            // extension instead ("taglib/mp3", "taglib/flac", ...), the exact scheme TagLib's own
            // extension-based resolution would have produced for the ORIGINAL file.
            var mimeType = "taglib/" + Path.GetExtension(plan.From).TrimStart('.').ToLowerInvariant();
            using (var file = TagLib.File.Create(tmpPath, mimeType, TagLib.ReadStyle.Average))
            {
                ApplyTagDiff(file.Tag, plan.TagDiff);
                file.Save();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            TryDelete(tmpPath, plan.MediaId);
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, DetailForFailure("io")), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        // Step 3 — THE COMMIT POINT: two same-directory (POSIX-atomic) renames swap the retagged tmp
        // in behind a same-directory backup of the original. The path plan.From never changes.
        try
        {
            File.Move(plan.From, bakPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tmpPath, plan.MediaId);
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, DetailForFailure("io")), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        try
        {
            File.Move(tmpPath, plan.From, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The rarer half-done shape: the original is at bak, tmp still holds the retag. Restore
            // before ever reporting anything — the caller must never observe the file simply gone.
            // T380 review R2 small-item 1a: the detail distinguishes "restored" (indistinguishable
            // from "nothing touched" — the end state IS identical) from "could not restore" (a real
            // stranded file, detail.revert = false, WARN with the media id only).
            var restored = TryMoveBack(bakPath, plan.From);
            if (!restored)
                logger.LogWarning("File action retag: could not restore media {MediaId} after a failed commit swap", plan.MediaId);
            TryDelete(tmpPath, plan.MediaId);
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, Step3bFailureDetail(restored)), postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        // Past the commit point: plan.From now holds the retagged bytes; bakPath holds the pre-retag
        // original. Every failure from here reverts via bakPath — SPEC F154.7's "failures leave the
        // file as it was" now holds for retag too, not just rename/move.
        long postSize;
        DateTime mtime;
        try
        {
            var postInfo = new FileInfo(plan.From);
            postSize = postInfo.Length;

            // T380 review N4: the WARN comparison below reads the RAW (untruncated) mtime — the
            // STORED value stays truncated (ScanMtime's own rule), but the WARN exists precisely to
            // catch sub-second staleness truncation would otherwise hide.
            var postRawMtime = postInfo.LastWriteTimeUtc;
            mtime = ScanMtime.TruncateToSeconds(postRawMtime);
            WarnIfStatLooksStale(plan.MediaId, preSize, preRawMtime, postSize, postRawMtime);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            return await RevertRetagAsync(plan, planToken, bakPath, "io", postCommitCt);
        }

        var doneAudit = BuildAudit(plan, planToken, FileActionOutcomeKind.Done, EmptyDetail);
        var affected = await repository.RelocateAsync(plan.MediaId, plan.Xmin, plan.From, postSize, mtime, doneAudit, postCommitCt);

        if (affected > 0)
        {
            // Step 6: delete the backup on success, in a call that never throws.
            TryDelete(bakPath, plan.MediaId);
            return new FileActionOutcome(FileActionOutcomeKind.Done, null);
        }

        return await RevertRetagAsync(plan, planToken, bakPath, "db", postCommitCt);
    }

    /// <summary>T380 review R2-2 — a pre-existing <c>*.gwbak</c> sibling for THIS file, under ANY
    /// per-attempt suffix. Fails OPEN on an enumeration error (permission, transient mount hiccup):
    /// the retag's own later steps will surface a real filesystem problem anyway, so an inconclusive
    /// answer here is not itself grounds for a refusal.</summary>
    static bool HasLeftoverBackup(string directory, string fileName)
    {
        try
        {
            return Directory.EnumerateFiles(directory, fileName + ".*.gwbak").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The TRUE retag revert (T380 review B1): <c>overwrite: true</c> deletes the retagged
    /// file currently sitting at <c>plan.From</c> ("the retagged leftover") and replaces it with the
    /// backup's bytes AND tags — a byte-for-byte restore of the pre-retag original, not merely an
    /// outcome label.</summary>
    async Task<FileActionOutcome> RevertRetagAsync(
        FileActionPlan plan, string planToken, string bakPath, string reason, CancellationToken postCommitCt)
    {
        try
        {
            File.Move(bakPath, plan.From, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("File action retag revert failed for media {MediaId}; the retagged file may remain at its path", plan.MediaId);
            await repository.AuditAsync(
                BuildAudit(plan, planToken, FileActionOutcomeKind.Failed, DetailForFailure(reason, revertSucceeded: false)),
                postCommitCt);
            return new FileActionOutcome(FileActionOutcomeKind.Failed, null);
        }

        await repository.AuditAsync(BuildAudit(plan, planToken, FileActionOutcomeKind.Reverted, DetailForFailure(reason)), postCommitCt);
        return new FileActionOutcome(FileActionOutcomeKind.Reverted, null);
    }

    static void ApplyTagDiff(TagLib.Tag tag, IReadOnlyList<TagChange> diff)
    {
        foreach (var change in diff)
        {
            switch (change.Field)
            {
                case "artist":
                    tag.Performers = [change.CatalogValue];
                    break;
                case "title":
                    tag.Title = change.CatalogValue;
                    break;
                case "album":
                    tag.Album = change.CatalogValue;
                    break;
                case "genre":
                    tag.Genres = [change.CatalogValue];
                    break;
                case "year":
                    // TagDiffCalculator never emits a "year" change with a non-numeric
                    // CatalogValue — TryParse is defensive, never a throw on this write path.
                    if (uint.TryParse(change.CatalogValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
                        tag.Year = year;
                    break;
            }
        }
    }

    /// <summary>SPEC F154.8's own NFS-posture WARN — logged, never affects the outcome or the stored
    /// stat (the fresh values are stored regardless). Backward mtime, or a Save that changed nothing
    /// observable, both suggest a stale stat cache.</summary>
    void WarnIfStatLooksStale(long mediaId, long preSize, DateTime preRawMtime, long postSize, DateTime postRawMtime)
    {
        if (postRawMtime < preRawMtime)
            logger.LogWarning(
                "File action retag: re-stat mtime moved backward for media {MediaId} — storing the fresh stat anyway", mediaId);
        else if (postSize == preSize && postRawMtime == preRawMtime)
            logger.LogWarning(
                "File action retag: no observable size/mtime change for media {MediaId} after a tag write — storing the fresh stat anyway",
                mediaId);
    }

    /// <summary>Deletes <paramref name="path"/> if present; never throws (T380 review B1's own "a
    /// finally that never throws" — used for both the tmp cleanup on a failed commit and the bak
    /// cleanup on success).</summary>
    void TryDelete(string path, long mediaId)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("File action: could not delete a temporary file for media {MediaId}", mediaId);
        }
    }

    /// <summary>Best-effort restore for the rare half-done commit-swap shape — returns whether it
    /// worked; the caller decides what to log.</summary>
    static bool TryMoveBack(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Builds the audit entry every outcome writes — <c>to_path</c> is <c>plan.To</c> for
    /// rename/move (already the canonical written path, whichever outcome this attempt landed on)
    /// and <see langword="null"/> for retag (SPEC F154.7: its destination is its source).</summary>
    static FileActionAuditEntry BuildAudit(FileActionPlan plan, string planToken, FileActionOutcomeKind outcome, string detailJson)
    {
        var toPath = plan.Verb == FileActionVerb.Retag ? null : plan.To;
        return new FileActionAuditEntry(
            plan.MediaId, plan.Verb, plan.From, toPath, planToken, FileActionOutcomeTokens.ToToken(outcome), detailJson);
    }

    static string DetailForRule(FileActionRule rule) =>
        JsonSerializer.Serialize(new { rule = FileActionRuleTokens.ToToken(rule) });

    static string DetailForFailure(string reason, bool? revertSucceeded = null) =>
        revertSucceeded is null
            ? JsonSerializer.Serialize(new { reason })
            : JsonSerializer.Serialize(new { reason, revert = revertSucceeded.Value });

    /// <summary>
    /// The step-3b commit-swap failure's own audit detail (T380 review R2 small-item 1a) — extracted
    /// as its own named, <c>internal</c> decision so the review's own "pin the audit shape by
    /// unit-testing the catch handler directly" fallback has something concrete to call: arranging a
    /// REAL race between the bak-move succeeding, the tmp-move failing, AND the restore-back also
    /// failing needs a mid-flight permission change between two synchronous <see cref="File.Move"/>
    /// calls with no async boundary in between — genuinely unarrangeable from a test without adding a
    /// test-only production seam, so this method is tested directly instead of through a live race.
    /// </summary>
    internal static string Step3bFailureDetail(bool restored) =>
        restored ? DetailForFailure("io") : DetailForFailure("io", revertSucceeded: false);
}
