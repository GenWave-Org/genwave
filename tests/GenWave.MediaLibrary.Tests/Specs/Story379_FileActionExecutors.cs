// STORY-379 — The gardener may fix my files, when I say so — the EXECUTOR half (SPEC F154.4,
// F154.6-F154.8 · PLAN T380; T380 review B1-B6, N1-N10)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture, REAL temp files/dirs/symlinks
// (TestMedia.CreateTone via ffmpeg), REAL ScanService.ScanOnceAsync ticks — no mocked filesystem, no
// mocked database. Every scenario runs a full plan → execute round trip through
// Garden.FileActions.FileActionExecutor + FileActionRepository, exactly the shape T381's confirm
// endpoint will call.
//
// "Zero drift" is read the MOST DIRECT way ScanService already exposes it: ScanOnceAsync enqueues
// exactly one media id per discovered OR changed file onto its own delta channel (never for an
// unchanged file), so an empty drain after a tick IS "0 discovered, 0 changed" directly — and, for
// every fact here, "0 missing" follows structurally: the executor updates a row's own path/size/mtime
// in the SAME transaction as the filesystem write, so there is never a moment where the catalog still
// points at a path the write already vacated for the scan to find missing.
//
// The audio-stream-untouched proof (AC4, and the flac/Xiph N8 fact) is `ffmpeg -map 0:a -c copy -f
// md5 -` before/after — the same ffmpeg TestMedia's own fixtures already shell out to. The retag
// REVERT proof (B1) is a full-FILE SHA-256 (tags included, not just the audio stream) — a true
// byte-for-byte restore, not merely a label.
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Garden.FileActions;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Scan;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFileActionExecutors
{
    static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    const string DefaultArtist = "Old Artist";
    const string DefaultTitle = "Old Title";
    const string DefaultAlbum = "Old Album";
    const string DefaultGenre = "Old Genre";
    const int DefaultYear = 2000;
    const string DefaultMood = "warm";

    static readonly UnixFileMode ReadExecuteOnly =
        UnixFileMode.UserRead | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    static readonly UnixFileMode ReadWriteExecute = ReadExecuteOnly | UnixFileMode.UserWrite;

    // ─────────────────────────────────────────────────────────────────────────
    // Shared scaffolding
    // ─────────────────────────────────────────────────────────────────────────

    // T381 review N4: the planner now reads a retag's own file tags itself, via IFileTagReader —
    // the REAL, production FileTagReader here (not a fake), since every fact in this file already
    // arranges a REAL mp3 on disk and wants a genuine TagLib read exactly like the endpoint gets.
    static FileActionPlanner BuildPlanner(string root) =>
        new(
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { MediaRoot = root }),
            new FakeOptionsMonitor<ScanOptions>(new ScanOptions()),
            new FileSystemProbe(),
            new FileTagReader());

    static FileActionRepository FileRepo(DatabaseFixture db) =>
        new(db.DataSource, NullLogger<FileActionRepository>.Instance);

    static FileActionExecutor BuildExecutor(
        DatabaseFixture db, string root, IScanGate? gate = null, IFileSystemProbe? probe = null, int gateTimeoutSeconds = 30,
        TimeSpan? postCommitBudget = null) =>
        new(
            gate ?? new ScanGate(),
            probe ?? new FileSystemProbe(),
            FileRepo(db),
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { MediaRoot = root }),
            new FakeOptionsMonitor<GardenerOptions>(new GardenerOptions
            {
                FileActions = new GardenerFileActionsOptions { GateTimeoutSeconds = gateTimeoutSeconds },
            }),
            NullLogger<FileActionExecutor>.Instance,
            postCommitBudget);

    /// <summary>A ready row: a real tagged mp3 (or, when <paramref name="fileName"/> names one, flac)
    /// under <c>{root}/a/</c>, discovered by a real scan tick and enriched to <c>ready</c> with
    /// non-null loudness/cue/bpm/mood facts (AC4's own "enrichment columns unchanged" fact needs real
    /// values to prove nothing moved).</summary>
    static async Task<(string Root, string SubjectDir, string SubjectPath, long MediaId, MediaRepository Repo)>
        ArrangeReadyRowAsync(DatabaseFixture db, string fileName = "x.mp3")
    {
        var repo = Harness.Repo(db);
        var root = TestMedia.NewTempDir();
        var subjectDir = Path.Combine(root, "a");
        Directory.CreateDirectory(subjectDir);
        var subjectPath = TestMedia.CreateTone(
            subjectDir, fileName, title: DefaultTitle, artist: DefaultArtist, album: DefaultAlbum,
            genre: DefaultGenre, year: DefaultYear);

        var (scan, queue) = Harness.Scanner(repo, root);
        await scan.ScanOnceAsync(CancellationToken.None);
        var mediaId = Assert.Single(Harness.DrainIds(queue));

        await repo.WriteEnrichmentAsync(mediaId, new EnrichmentResult(
            DurationMs: 2_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 320,
            Title: DefaultTitle, Artist: DefaultArtist, Album: DefaultAlbum, AlbumArtist: DefaultArtist,
            Genre: DefaultGenre, TrackNo: 1, Year: DefaultYear,
            Explicit: null,
            IntegratedLufs: -14.0, TruePeakDbtp: -1.0, Measurable: true,
            CueInSec: 0.1, CueOutSec: 1.8, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: 120.0, BpmAnalyzedAt: DateTime.UtcNow),
            CancellationToken.None);
        await repo.WriteMoodsAsync(mediaId, [DefaultMood], CancellationToken.None);

        return (root, subjectDir, subjectPath, mediaId, repo);
    }

    /// <summary>Re-reads the row's <c>(xmin, path, library_id)</c> to build a plan-ready
    /// <see cref="FileActionSubject"/> — the file's own current tags are no longer carried here
    /// (T381 review N4: <see cref="FileActionPlanner"/> reads them itself, via
    /// <see cref="BuildPlanner"/>'s own real <c>FileTagReader</c>). The catalog fields default to
    /// <see cref="ArrangeReadyRowAsync"/>'s own written values — a caller overrides exactly the
    /// field(s) a fact is about; <see langword="null"/> is a real, expressible override (SPEC F154.1:
    /// no catalog opinion on that field), never silently replaced by the row's current value.</summary>
    static async Task<FileActionSubject> LoadSubjectAsync(
        DatabaseFixture db, long mediaId,
        string? artist = DefaultArtist, string? title = DefaultTitle, string? album = DefaultAlbum,
        int? year = DefaultYear, string? genre = DefaultGenre)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<(string Xmin, string Path, long LibraryId)>(
            "select xmin::text as xmin, path, library_id from library.media where id = @mediaId", new { mediaId });

        return new FileActionSubject(mediaId, row.Xmin, row.Path, row.LibraryId, artist, title, album, year, genre);
    }

    /// <summary>Plans <paramref name="verb"/> against the row's current binding and executes it in
    /// one call — the shape every fact below that isn't specifically about the plan/execute GAP uses.
    /// Fails the arrange (never the fact) if the planner itself refuses — every caller here hands it
    /// an already-known-good shape.</summary>
    static async Task<(FileActionPlan Plan, FileActionOutcome Outcome)> ExecuteVerbAsync(
        DatabaseFixture db, string root, long mediaId, FileActionVerb verb, string? target = null,
        string? artist = DefaultArtist, string? title = DefaultTitle, string? album = DefaultAlbum,
        int? year = DefaultYear, string? genre = DefaultGenre, string planToken = "test-plan-token")
    {
        var planner = BuildPlanner(root);
        var subject = await LoadSubjectAsync(db, mediaId, artist, title, album, year, genre);
        var planResult = planner.Plan(subject, verb, target, Now);
        Assert.False(planResult.IsRefused);
        var plan = planResult.Plan!;

        var executor = BuildExecutor(db, root);
        var outcome = await executor.ExecuteAsync(plan, planToken, CancellationToken.None);
        return (plan, outcome);
    }

    static FileTags ReadFileTags(string path)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        return new FileTags(
            TagText.Normalize(tag.JoinedPerformers),
            TagText.Normalize(tag.Title),
            TagText.Normalize(tag.Album),
            tag.Year > 0 ? tag.Year : null,
            TagText.Normalize(tag.JoinedGenres));
    }

    /// <summary>The audio STREAM's own content hash — <c>-map 0:a -c copy</c> never re-encodes, so
    /// this changes if and only if the audio bytes themselves changed; a tag-only rewrite leaves it
    /// identical (AC4's own "audio untouched by construction" proof).</summary>
    static string AudioMd5(string path)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-nostats", "-hide_banner", "-loglevel", "error", "-i", path, "-map", "0:a", "-c", "copy", "-f", "md5", "-" })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg md5 failed: {stderr.Result}");
        return stdout.Result.Trim();
    }

    /// <summary>The WHOLE file's own content hash (tags included) — B1's own "byte-for-byte restore,
    /// not merely a label" proof for a retag revert.</summary>
    static string FileSha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    static async Task<(string Path, long SizeBytes, DateTime Mtime)> ReadRowStatAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(string, long, DateTime)>(
            "select path, size_bytes, mtime from library.media where id = @mediaId", new { mediaId });
    }

    static async Task<string> ReadXminAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string>("select xmin::text from library.media where id = @mediaId", new { mediaId })
            ?? throw new InvalidOperationException("row not found");
    }

    /// <summary>The six enrichment columns AC4 requires unchanged, flattened to plain comparable
    /// values — <c>moods</c> (a Postgres array) joined to one string so the whole read stays a single,
    /// safely-comparable tuple for one <c>Assert.Equal</c> call.</summary>
    static async Task<(double? IntegratedLufs, double? CueInSec, double? CueOutSec, double? TrackEnergy, double? Bpm, string Moods)>
        ReadEnrichmentAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<(double? IntegratedLufs, double? CueInSec, double? CueOutSec, double? TrackEnergy, double? Bpm, string[]? Moods)>(
            "select integrated_lufs, cue_in_sec, cue_out_sec, track_energy, bpm, moods from library.media where id = @mediaId",
            new { mediaId });
        return (row.IntegratedLufs, row.CueInSec, row.CueOutSec, row.TrackEnergy, row.Bpm, string.Join(",", row.Moods ?? []));
    }

    static async Task PatchTitleAsync(DatabaseFixture db, long mediaId, string newTitle)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("update library.media set title = @newTitle where id = @mediaId", new { mediaId, newTitle });
    }

    /// <summary>
    /// Arms a real Postgres failure (AC13/T380 review B1: "a real DB failure, not a fake") — a
    /// <c>BEFORE</c> trigger on <paramref name="table"/> that unconditionally raises, optionally
    /// scoped by <paramref name="whenClause"/> and optionally preceded by a <c>pg_sleep</c> so a test
    /// can inject a filesystem change into the deterministic window between the triggering statement
    /// starting and the trigger actually firing (T380 review N8's own revert-failure fact).
    /// <see cref="DisarmTriggerAsync"/> drops both objects.
    ///
    /// <para>
    /// T380 review N9's own "parameterize the test DDL helper": generalized out of two near-duplicate
    /// hardcoded triggers (one on <c>library.media</c>'s UPDATE, one on <c>library.file_action</c>'s
    /// INSERT) into this one reusable shape. <paramref name="whenClause"/> is interpolated (DDL
    /// <c>WHEN</c> clauses cannot bind query parameters in Postgres — there is no prepare/execute
    /// protocol for a CREATE TRIGGER statement) — every call site here only ever interpolates a
    /// database-generated <c>long</c> media id, which can only ever render as ASCII digits (and an
    /// optional leading <c>-</c>), so there is no injectable surface despite the string
    /// concatenation.
    /// </para>
    /// </summary>
    static async Task ArmFailingTriggerAsync(
        DatabaseFixture db, string triggerName, string table, string triggerEvent, string? whenClause, TimeSpan? sleep = null)
    {
        var sleepSql = sleep is { } s
            ? $"perform pg_sleep({s.TotalSeconds.ToString(CultureInfo.InvariantCulture)});"
            : "";
        var whenSql = whenClause is null ? "" : $"when ({whenClause})";

        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync($"""
            create or replace function library.{triggerName}() returns trigger
            language plpgsql as $trig$
            begin
              {sleepSql}
              raise exception 'T380 test-induced failure';
            end;
            $trig$;

            drop trigger if exists {triggerName} on {table};
            create trigger {triggerName}
              before {triggerEvent} on {table}
              for each row
              {whenSql}
              execute function library.{triggerName}();
            """);
    }

    static async Task DisarmTriggerAsync(DatabaseFixture db, string triggerName, string table)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync($"""
            drop trigger if exists {triggerName} on {table};
            drop function if exists library.{triggerName}();
            """);
    }

    static async Task<(ScanGate Gate, BlockingFileSystemProbe Probe, Task<FileActionOutcome> FirstCall, string Root, MediaRepository Repo, FileActionPlan Plan)>
        ArrangeHeldGateAsync(DatabaseFixture db)
    {
        var (root, _, _, mediaId, repo) = await ArrangeReadyRowAsync(db);
        var planner = BuildPlanner(root);
        var subject = await LoadSubjectAsync(db, mediaId);
        var plan = planner.Plan(subject, FileActionVerb.Rename, null, Now).Plan!;

        var gate = new ScanGate();
        var probe = new BlockingFileSystemProbe(new FileSystemProbe());
        var executor = BuildExecutor(db, root, gate: gate, probe: probe);

        var firstCall = Task.Run(() => executor.ExecuteAsync(plan, "tok-gate-1", CancellationToken.None));
        probe.WaitUntilEntered();

        return (gate, probe, firstCall, root, repo, plan);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC3 — rename
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRename(DatabaseFixture db)
    {
        [Fact]
        public async Task TheFileEndsUpAtTheNewPath()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.True(File.Exists(plan.To));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task TheRowsPathSizeAndMtimeMatchTheFilesStat()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                var info = new FileInfo(plan.To);
                var expected = (plan.To, info.Length, ScanMtime.TruncateToSeconds(info.LastWriteTimeUtc));

                Assert.Equal(expected, await ReadRowStatAsync(db, mediaId));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task AScanTickAfterRenameReportsNoDrift()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, repo) = await ArrangeReadyRowAsync(db);
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                var (scan, queue) = Harness.Scanner(repo, root);
                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Empty(Harness.DrainIds(queue));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task StateStaysReadyAfterRename()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.Equal("ready", await Harness.StateOfAsync(db, mediaId));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC4 — retag
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetag(DatabaseFixture db)
    {
        [Fact]
        public async Task TagLibReadsTheNewArtistAfterward()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.Equal("A", ReadFileTags(plan.From).Artist);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task TheAudioStreamIsByteIdentical()
        {
            await db.ResetAsync();
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var before = AudioMd5(subjectPath);

                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.Equal(before, AudioMd5(plan.From));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task EnrichmentColumnsAreUnchanged()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var before = await ReadEnrichmentAsync(db, mediaId);

                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.Equal(before, await ReadEnrichmentAsync(db, mediaId));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task AScanTickAfterRetagReportsNoDrift()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, repo) = await ArrangeReadyRowAsync(db);
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                var (scan, queue) = Harness.Scanner(repo, root);
                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Empty(Harness.DrainIds(queue));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task StateStaysReadyAfterRetag()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.Equal("ready", await Harness.StateOfAsync(db, mediaId));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetagNeverBlanks(DatabaseFixture db)
    {
        [Fact]
        public async Task ANullCatalogAlbumLeavesTheFilesAlbumUntouched()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A", album: null);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.Equal(DefaultAlbum, ReadFileTags(plan.From).Album);
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>T380 review N8 — a flac (Xiph/Vorbis comment) retag, not just mp3/ID3, still leaves
    /// the audio stream untouched.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetagFlac(DatabaseFixture db)
    {
        [Fact]
        public async Task TheAudioStreamIsByteIdenticalForFlac()
        {
            await db.ResetAsync();
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db, fileName: "x.flac");
            try
            {
                var before = AudioMd5(subjectPath);

                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.Equal(before, AudioMd5(plan.From));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>T380 review B1 — a retag reverted after a real DB failure restores the ORIGINAL bytes
    /// AND tags, byte-for-byte, via its own <c>.gwbak</c> backup — a true revert, not merely a label.
    /// No separate "simulate a mid-retag crash" fact exists: the copy-retag-swap shape (SPEC F154.x's
    /// own rider) makes a mid-Save crash structurally safe on its own — only the <c>.gwtmp</c> copy is
    /// ever at risk, and it is never swapped in until AFTER Save succeeds.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetagRevert(DatabaseFixture db)
    {
        const string TriggerName = "t380_media_update";

        static async Task<(FileActionOutcome Outcome, string SubjectPath, long MediaId, string Root, string BeforeHash)>
            ArrangeAndExecuteAsync(DatabaseFixture db)
        {
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var beforeHash = FileSha256(subjectPath);
            await ArmFailingTriggerAsync(db, TriggerName, "library.media", "update", $"old.id = {mediaId}");
            var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
            return (outcome, subjectPath, mediaId, root, beforeHash);
        }

        [Fact]
        public async Task TheFileBytesAreByteIdenticalToTheOriginalAfterRevert()
        {
            await db.ResetAsync();
            var (outcome, subjectPath, _, root, beforeHash) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.Equal(FileActionOutcomeKind.Reverted, outcome.Kind);
                Assert.Equal(beforeHash, FileSha256(subjectPath));
            }
            finally
            {
                await DisarmTriggerAsync(db, TriggerName, "library.media");
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task AuditRecordsRevertedWithReasonDb()
        {
            await db.ResetAsync();
            var (outcome, _, mediaId, root, _) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.Equal(FileActionOutcomeKind.Reverted, outcome.Kind);

                var rows = await FileRepo(db).ListAuditAsync(mediaId, 10, CancellationToken.None);
                var row = Assert.Single(rows);
                var reason = JsonDocument.Parse(row.Detail).RootElement.GetProperty("reason").GetString();

                Assert.Equal(("reverted", "db"), (row.Outcome, reason));
            }
            finally
            {
                await DisarmTriggerAsync(db, TriggerName, "library.media");
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>T380 review R2-2 — a leftover <c>.gwbak</c> (from a prior attempt that never cleaned
    /// up: a revert-failure by design, a failed delete, or a crash mid-attempt) makes a retag
    /// diagnosable instead of silently colliding with — or being masked by — a fresh attempt's own
    /// per-attempt-unique tmp/bak names.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetagLeftoverBackup(DatabaseFixture db)
    {
        [Fact]
        public async Task ALeftoverBackupRefusesTheRetag()
        {
            await db.ResetAsync();
            var (root, subjectDir, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var leftoverBak = Path.Combine(subjectDir, Path.GetFileName(subjectPath) + ".deadbeef.gwbak");
            File.WriteAllBytes(leftoverBak, [1, 2, 3]);
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");

                Assert.Equal((FileActionOutcomeKind.Refused, FileActionRule.LeftoverBackup), (outcome.Kind, outcome.Rule));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>T380 review round 3 item 1 — the audit row's own <c>detail.rule</c> is the
        /// SNAKE_CASE wire token (<c>FileActionRuleTokens</c>), not the raw PascalCase
        /// <see cref="Enum.ToString()"/> spelling.</summary>
        [Fact]
        public async Task TheAuditDetailNamesTheRuleInSnakeCase()
        {
            await db.ResetAsync();
            var (root, subjectDir, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var leftoverBak = Path.Combine(subjectDir, Path.GetFileName(subjectPath) + ".deadbeef.gwbak");
            File.WriteAllBytes(leftoverBak, [1, 2, 3]);
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Refused, outcome.Kind);

                var rows = await FileRepo(db).ListAuditAsync(mediaId, 10, CancellationToken.None);
                var row = Assert.Single(rows);
                var rule = JsonDocument.Parse(row.Detail).RootElement.GetProperty("rule").GetString();

                Assert.Equal("leftover_backup", rule);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task AfterACleanSuccessTheNextRetagSucceedsWithNoResidue()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (_, firstOutcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "A");
                Assert.Equal(FileActionOutcomeKind.Done, firstOutcome.Kind);

                var (_, secondOutcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Retag, artist: "B");

                Assert.Equal(FileActionOutcomeKind.Done, secondOutcome.Kind);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task AScanTickIgnoresALeftoverBackupRegardlessOfItsRandomSuffix()
        {
            await db.ResetAsync();
            var (root, subjectDir, subjectPath, _, repo) = await ArrangeReadyRowAsync(db);
            var leftoverBak = Path.Combine(subjectDir, Path.GetFileName(subjectPath) + ".deadbeef.gwbak");
            File.WriteAllBytes(leftoverBak, [1, 2, 3]);
            try
            {
                var (scan, queue) = Harness.Scanner(repo, root);
                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Empty(Harness.DrainIds(queue));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>T380 review R2 small-item 1a — the step-3b commit-swap failure's own audit detail,
    /// pinned DIRECTLY: a live race between the bak-move succeeding, the tmp-move failing, AND the
    /// restore-back ALSO failing needs a mid-flight permission change between two synchronous
    /// <see cref="File.Move(string, string, bool)"/> calls with no async boundary in between to
    /// inject into — genuinely unarrangeable from a test without adding a test-only production seam
    /// (confirmed by the SAME successfully-arranged race in <c>ScenarioRevertFailure</c>, which needs
    /// exactly one such boundary and gets it from a real 500ms <c>pg_sleep</c> — no equivalent slow
    /// step exists between steps 3a and 3b, which never touch the database). Exercises
    /// <c>FileActionExecutor.Step3bFailureDetail</c> directly instead — no DB, no filesystem.
    /// </summary>
    public sealed class ScenarioStep3bFailureDetail
    {
        [Fact]
        public void WhenTheRestoreFailsTheDetailNamesRevertFalse()
        {
            var detail = FileActionExecutor.Step3bFailureDetail(restored: false);
            var root = JsonDocument.Parse(detail).RootElement;

            Assert.Equal(("io", false), (root.GetProperty("reason").GetString(), root.GetProperty("revert").GetBoolean()));
        }

        [Fact]
        public void WhenTheRestoreSucceedsTheDetailCarriesNoRevertFlag()
        {
            var detail = FileActionExecutor.Step3bFailureDetail(restored: true);
            var root = JsonDocument.Parse(detail).RootElement;

            Assert.False(root.TryGetProperty("revert", out _));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC5 — move
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMove(DatabaseFixture db)
    {
        [Fact]
        public async Task TheFileEndsUpInTheTargetDirectory()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            var targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
            try
            {
                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Move, target: targetDir);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                Assert.True(File.Exists(plan.To));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task TheRowsPathFollowsTheMove()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            var targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
            try
            {
                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Move, target: targetDir);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                var (path, _, _) = await ReadRowStatAsync(db, mediaId);
                Assert.Equal(plan.To, path);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task AScanTickAfterMoveReportsNoDrift()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, repo) = await ArrangeReadyRowAsync(db);
            var targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Move, target: targetDir);
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                var (scan, queue) = Harness.Scanner(repo, root);
                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Empty(Harness.DrainIds(queue));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>T380 review B6 — the planner itself now refuses a move into a symlinked directory
    /// (<c>Story379_FileActionPlannerAndJail.cs</c>'s own <c>ScenarioMoveTargetIsASymlinkedDirectory</c>
    /// carries the planner-level proof); this class confirms the SAME shape never even reaches the
    /// executor — <c>ExecuteVerbAsync</c>'s own arrange-sanity assert would fail loudly if it did.
    /// gh-#650 (the scanner's own double-enumeration of a symlinked alias directory) is CLOSED by
    /// this refusal for every FUTURE file action; the scanner's own behavior is unrelated and stays
    /// out of scope here.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMoveIntoASymlinkedDirectory(DatabaseFixture db)
    {
        [Fact]
        public async Task AMoveIntoASymlinkedDirectoryIsRefused()
        {
            await db.ResetAsync();
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var innerDir = Path.Combine(root, "inner");
            Directory.CreateDirectory(innerDir);
            var linkDir = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(linkDir, innerDir);
            try
            {
                var planner = BuildPlanner(root);
                var subject = await LoadSubjectAsync(db, mediaId);

                var result = planner.Plan(subject, FileActionVerb.Move, linkDir, Now);

                Assert.Equal(FileActionRule.SymlinkedTarget, result.Refusal!.Value.Rule);
                Assert.True(File.Exists(subjectPath));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC6 — audit
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAudit(DatabaseFixture db)
    {
        [Fact]
        public async Task OneFileActionRowCarriesVerbFromToTokenAndOutcome()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            try
            {
                var (plan, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename, planToken: "tok-audit");
                Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind);

                var rows = await FileRepo(db).ListAuditAsync(mediaId, 10, CancellationToken.None);
                var row = Assert.Single(rows);

                Assert.Equal(
                    (FileActionVerb.Rename, plan.From, plan.To, "tok-audit", "done"),
                    (row.Verb, row.FromPath, row.ToPath, row.PlanToken, row.Outcome));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>T380 review N8 — a Busy outcome still writes its own audit row. Also R2-1's own
    /// "Busy after a full gate timeout still writes its audit row" — the second executor's own
    /// GateTimeoutSeconds elapses IN FULL before Busy is reported, so the post-commit token backing
    /// this audit write is built only after that complete wait, never stale from before it.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBusyAudit(DatabaseFixture db)
    {
        [Fact]
        public async Task ABusyOutcomeWritesAnAuditRow()
        {
            await db.ResetAsync();
            var (gate, probe, firstCall, root, _, plan) = await ArrangeHeldGateAsync(db);
            try
            {
                var executor2 = BuildExecutor(db, root, gate: gate, gateTimeoutSeconds: 1);

                var outcome = await executor2.ExecuteAsync(plan, "tok-busy-2", CancellationToken.None);
                Assert.Equal(FileActionOutcomeKind.Busy, outcome.Kind);

                var rows = await FileRepo(db).ListAuditAsync(plan.MediaId, 10, CancellationToken.None);
                Assert.Contains(rows, r => r.Outcome == "busy" && r.PlanToken == "tok-busy-2");
            }
            finally
            {
                probe.Release();
                await firstCall;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>T380 review N8 — the row update rolls back too when the AUDIT insert (same
    /// transaction) fails; the failure is never partially applied.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAuditInsertFailureRollback(DatabaseFixture db)
    {
        const string TriggerName = "t380_file_action_insert";

        [Fact]
        public async Task TheRowUpdateRollsBackWhenTheAuditInsertFails()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            var originalPath = (await ReadRowStatAsync(db, mediaId)).Path;
            await ArmFailingTriggerAsync(db, TriggerName, "library.file_action", "insert", whenClause: null);
            try
            {
                await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);

                var (path, _, _) = await ReadRowStatAsync(db, mediaId);
                Assert.Equal(originalPath, path);
            }
            finally
            {
                await DisarmTriggerAsync(db, TriggerName, "library.file_action");
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC13 — revert on a real database failure
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRevert(DatabaseFixture db)
    {
        const string TriggerName = "t380_media_update";

        [Fact]
        public async Task TheFileIsBackAtTheOriginalPath()
        {
            await db.ResetAsync();
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            await ArmFailingTriggerAsync(db, TriggerName, "library.media", "update", $"old.id = {mediaId}");
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);
                Assert.Equal(FileActionOutcomeKind.Reverted, outcome.Kind);

                Assert.True(File.Exists(subjectPath));
            }
            finally
            {
                await DisarmTriggerAsync(db, TriggerName, "library.media");
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task TheRowIsUnchanged()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            var beforeXmin = await ReadXminAsync(db, mediaId);
            await ArmFailingTriggerAsync(db, TriggerName, "library.media", "update", $"old.id = {mediaId}");
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);
                Assert.Equal(FileActionOutcomeKind.Reverted, outcome.Kind);

                Assert.Equal(beforeXmin, await ReadXminAsync(db, mediaId));
            }
            finally
            {
                await DisarmTriggerAsync(db, TriggerName, "library.media");
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task OneAuditRowRecordsRevertedWithReasonDb()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            await ArmFailingTriggerAsync(db, TriggerName, "library.media", "update", $"old.id = {mediaId}");
            try
            {
                var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Rename);
                Assert.Equal(FileActionOutcomeKind.Reverted, outcome.Kind);

                var rows = await FileRepo(db).ListAuditAsync(mediaId, 10, CancellationToken.None);
                var row = Assert.Single(rows);
                var reason = JsonDocument.Parse(row.Detail).RootElement.GetProperty("reason").GetString();

                Assert.Equal(("reverted", "db"), (row.Outcome, reason));
            }
            finally
            {
                await DisarmTriggerAsync(db, TriggerName, "library.media");
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>T380 review N8 — when the REVERT's own filesystem move ALSO fails (here: the
    /// original's parent directory turns read-only in the deterministic window a slow trigger opens,
    /// right after the forward move but before the DB write is even attempted), the outcome is
    /// Failed with <c>detail.revert = false</c> — never a silent Reverted that didn't actually
    /// happen.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRevertFailure(DatabaseFixture db)
    {
        const string TriggerName = "t380_media_update_slow";

        [Fact]
        public async Task TheOutcomeIsFailedWithRevertFalse()
        {
            await db.ResetAsync();
            var (root, subjectDir, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
            await ArmFailingTriggerAsync(
                db, TriggerName, "library.media", "update", $"old.id = {mediaId}", sleep: TimeSpan.FromMilliseconds(500));
            try
            {
                var planner = BuildPlanner(root);
                var subject = await LoadSubjectAsync(db, mediaId);
                var plan = planner.Plan(subject, FileActionVerb.Move, targetDir, Now).Plan!;
                var executor = BuildExecutor(db, root);

                var moveTask = Task.Run(() => executor.ExecuteAsync(plan, "tok-revert-failure", CancellationToken.None));

                // The trigger's own 500ms pg_sleep guarantees this window is real: the forward move
                // (source dir -> target dir) always completes well before the DB call even reports
                // failure, let alone before the executor attempts its own revert move back.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (!File.Exists(plan.To) && DateTime.UtcNow < deadline)
                    await Task.Delay(10);
                Assert.True(File.Exists(plan.To));

                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(subjectDir, ReadExecuteOnly);

                var outcome = await moveTask;

                Assert.Equal(FileActionOutcomeKind.Failed, outcome.Kind);

                var rows = await FileRepo(db).ListAuditAsync(mediaId, 10, CancellationToken.None);
                var row = Assert.Single(rows);
                var revert = JsonDocument.Parse(row.Detail).RootElement.GetProperty("revert").GetBoolean();
                Assert.False(revert);
            }
            finally
            {
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(subjectDir, ReadWriteExecute);
                await DisarmTriggerAsync(db, TriggerName, "library.media");
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Conflict — the row was written between plan and execute
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioConflict(DatabaseFixture db)
    {
        static async Task<(FileActionPlan Plan, FileActionOutcome Outcome, string SubjectPath, string Root)> ArrangeAndExecuteAsync(DatabaseFixture db)
        {
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var planner = BuildPlanner(root);
            var subject = await LoadSubjectAsync(db, mediaId);
            var plan = planner.Plan(subject, FileActionVerb.Rename, null, Now).Plan!;

            // The row is written AFTER the plan was built but BEFORE it is confirmed — the exact
            // TOCTOU gap SPEC F154.5/STORY-379 AC7 exists to close (xmin changes underneath the plan).
            await PatchTitleAsync(db, mediaId, "Changed Between Plan And Execute");

            var executor = BuildExecutor(db, root);
            var outcome = await executor.ExecuteAsync(plan, "tok-conflict", CancellationToken.None);
            return (plan, outcome, subjectPath, root);
        }

        [Fact]
        public async Task OutcomeIsConflict()
        {
            await db.ResetAsync();
            var (_, outcome, _, root) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.Equal(FileActionOutcomeKind.Conflict, outcome.Kind);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task TheFileIsUntouched()
        {
            await db.ResetAsync();
            var (_, _, subjectPath, root) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.True(File.Exists(subjectPath));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task AuditRecordsConflict()
        {
            await db.ResetAsync();
            var (plan, _, _, root) = await ArrangeAndExecuteAsync(db);
            try
            {
                var rows = await FileRepo(db).ListAuditAsync(plan.MediaId, 10, CancellationToken.None);
                Assert.Equal("conflict", Assert.Single(rows).Outcome);
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Target appeared since the plan
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTargetAppearedSincePlan(DatabaseFixture db)
    {
        static async Task<(FileActionOutcome Outcome, string SubjectPath, string Root)> ArrangeAndExecuteAsync(DatabaseFixture db)
        {
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var planner = BuildPlanner(root);
            var subject = await LoadSubjectAsync(db, mediaId);
            var plan = planner.Plan(subject, FileActionVerb.Rename, null, Now).Plan!;

            // Written directly, never through the executor — the never-overwrite check under test is
            // the executor's own RE-PROBE right before the write (F154.4), not the planner's.
            await File.WriteAllBytesAsync(plan.To, [9, 9, 9]);

            var executor = BuildExecutor(db, root);
            var outcome = await executor.ExecuteAsync(plan, "tok-target-exists", CancellationToken.None);
            return (outcome, subjectPath, root);
        }

        [Fact]
        public async Task OutcomeIsRefusedTargetExists()
        {
            await db.ResetAsync();
            var (outcome, _, root) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.Equal((FileActionOutcomeKind.Refused, FileActionRule.TargetExists), (outcome.Kind, outcome.Rule));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task TheFileIsUntouched()
        {
            await db.ResetAsync();
            var (_, subjectPath, root) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.True(File.Exists(subjectPath));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>T380 review B2 — a read-only target directory (chmod 555) makes <c>File.Move</c>
    /// throw <see cref="UnauthorizedAccessException"/>, caught beside <see cref="IOException"/>.
    /// </summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMoveToReadOnlyDirectory(DatabaseFixture db)
    {
        static async Task<(FileActionOutcome Outcome, string SubjectPath, string TargetDir, string Root, long MediaId)>
            ArrangeAndExecuteAsync(DatabaseFixture db)
        {
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(targetDir, ReadExecuteOnly);

            var (_, outcome) = await ExecuteVerbAsync(db, root, mediaId, FileActionVerb.Move, target: targetDir);
            return (outcome, subjectPath, targetDir, root, mediaId);
        }

        static void Cleanup(string targetDir, string root)
        {
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(targetDir, ReadWriteExecute);
            Directory.Delete(root, recursive: true);
        }

        [Fact]
        public async Task OutcomeIsFailed()
        {
            await db.ResetAsync();
            var (outcome, _, targetDir, root, _) = await ArrangeAndExecuteAsync(db);
            try { Assert.Equal(FileActionOutcomeKind.Failed, outcome.Kind); }
            finally { Cleanup(targetDir, root); }
        }

        [Fact]
        public async Task TheFileStaysAtTheOriginalPath()
        {
            await db.ResetAsync();
            var (_, subjectPath, targetDir, root, _) = await ArrangeAndExecuteAsync(db);
            try { Assert.True(File.Exists(subjectPath)); }
            finally { Cleanup(targetDir, root); }
        }

        [Fact]
        public async Task AuditRowIsPresent()
        {
            await db.ResetAsync();
            var (_, _, targetDir, root, mediaId) = await ArrangeAndExecuteAsync(db);
            try
            {
                var rows = await FileRepo(db).ListAuditAsync(mediaId, 10, CancellationToken.None);
                Assert.Equal("failed", Assert.Single(rows).Outcome);
            }
            finally { Cleanup(targetDir, root); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cancellation (T380 review B3)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCancellation(DatabaseFixture db)
    {
        static async Task<(FileActionOutcome Outcome, string SubjectPath, string Root)> ArrangeWithPreCancelledTokenAsync(DatabaseFixture db)
        {
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var planner = BuildPlanner(root);
            var subject = await LoadSubjectAsync(db, mediaId);
            var plan = planner.Plan(subject, FileActionVerb.Rename, null, Now).Plan!;
            var executor = BuildExecutor(db, root);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            var outcome = await executor.ExecuteAsync(plan, "tok-precancelled", cts.Token);
            return (outcome, subjectPath, root);
        }

        [Fact]
        public async Task APreCancelledTokenReportsFailed()
        {
            await db.ResetAsync();
            var (outcome, _, root) = await ArrangeWithPreCancelledTokenAsync(db);
            try { Assert.Equal(FileActionOutcomeKind.Failed, outcome.Kind); }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task APreCancelledTokenTouchesNothing()
        {
            await db.ResetAsync();
            var (_, subjectPath, root) = await ArrangeWithPreCancelledTokenAsync(db);
            try { Assert.True(File.Exists(subjectPath)); }
            finally { Directory.Delete(root, recursive: true); }
        }

        static async Task<(FileActionOutcome Outcome, FileActionPlan Plan, string Root)> ArrangeWithCancelAfterProbeAsync(DatabaseFixture db)
        {
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            var planner = BuildPlanner(root);
            var subject = await LoadSubjectAsync(db, mediaId);
            var plan = planner.Plan(subject, FileActionVerb.Rename, null, Now).Plan!;

            var probe = new BlockingFileSystemProbe(new FileSystemProbe());
            using var cts = new CancellationTokenSource();
            var executor = BuildExecutor(db, root, probe: probe);

            var executeTask = Task.Run(() => executor.ExecuteAsync(plan, "tok-cancel-after", cts.Token));
            probe.WaitUntilEntered();
            await cts.CancelAsync();
            probe.Release();

            var outcome = await executeTask;
            return (outcome, plan, root);
        }

        [Fact]
        public async Task CancellingAfterTheTargetProbeStillReportsDone()
        {
            await db.ResetAsync();
            var (outcome, _, root) = await ArrangeWithCancelAfterProbeAsync(db);
            try { Assert.Equal(FileActionOutcomeKind.Done, outcome.Kind); }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task CancellingAfterTheTargetProbeStillUpdatesTheRow()
        {
            await db.ResetAsync();
            var (_, plan, root) = await ArrangeWithCancelAfterProbeAsync(db);
            try
            {
                var (path, _, _) = await ReadRowStatAsync(db, plan.MediaId);
                Assert.Equal(plan.To, path);
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cross-device (T380 review B4)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCrossDevice(DatabaseFixture db)
    {
        static async Task<(FileActionOutcome Outcome, string SubjectPath, string Root)> ArrangeAndExecuteAsync(DatabaseFixture db)
        {
            var (root, _, subjectPath, mediaId, _) = await ArrangeReadyRowAsync(db);
            var targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);

            var planner = BuildPlanner(root);
            var subject = await LoadSubjectAsync(db, mediaId);
            var plan = planner.Plan(subject, FileActionVerb.Move, targetDir, Now).Plan!;

            // No real bind mount is arrangeable in a test (T380 review B4's own ruling) — the
            // recording probe LIES about the device id for each side instead, proving SameDevice is
            // genuinely consulted before File.Move ever runs.
            var probe = new RecordingFileSystemProbe(new FileSystemProbe());
            probe.DeviceIdOverrides[plan.From] = 1;
            probe.DeviceIdOverrides[targetDir] = 2;

            var executor = BuildExecutor(db, root, probe: probe);
            var outcome = await executor.ExecuteAsync(plan, "tok-cross-device", CancellationToken.None);
            return (outcome, subjectPath, root);
        }

        [Fact]
        public async Task OutcomeIsRefusedCrossDevice()
        {
            await db.ResetAsync();
            var (outcome, _, root) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.Equal((FileActionOutcomeKind.Refused, FileActionRule.CrossDevice), (outcome.Kind, outcome.Rule));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public async Task TheFileIsUntouched()
        {
            await db.ResetAsync();
            var (_, subjectPath, root) = await ArrangeAndExecuteAsync(db);
            try
            {
                Assert.True(File.Exists(subjectPath));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>T380 review R2-3 — on Linux (this test box, always — the arrange-sanity assert says
    /// so), an INCONCLUSIVE device lookup refuses the move rather than silently proceeding. Off
    /// Linux this SAME inconclusive answer would SKIP the check instead
    /// (<c>FileSystemProbe.TryGetDeviceId</c>'s own always-false-off-Linux behaviour, unit-provable
    /// by reading that method, never by a fact on this box — there is no off-Linux CI lane here to
    /// run one on).</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDeviceLookupInconclusive(DatabaseFixture db)
    {
        [Fact]
        public async Task AnInconclusiveDeviceLookupOnLinuxIsRefused()
        {
            await db.ResetAsync();
            Assert.True(OperatingSystem.IsLinux());
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            var targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
            try
            {
                var planner = BuildPlanner(root);
                var subject = await LoadSubjectAsync(db, mediaId);
                var plan = planner.Plan(subject, FileActionVerb.Move, targetDir, Now).Plan!;

                // Simulates a failed statx (never a lie about the VALUE — DeviceIdUnknownPaths, not
                // DeviceIdOverrides) while genuinely running on Linux.
                var probe = new RecordingFileSystemProbe(new FileSystemProbe());
                probe.DeviceIdUnknownPaths.Add(plan.From);

                var executor = BuildExecutor(db, root, probe: probe);
                var outcome = await executor.ExecuteAsync(plan, "tok-device-unknown", CancellationToken.None);

                Assert.Equal((FileActionOutcomeKind.Refused, FileActionRule.CrossDevice), (outcome.Kind, outcome.Rule));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Root re-assertion (T380 review N2)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRootReassertion(DatabaseFixture db)
    {
        [Fact]
        public async Task AForgedPlanOutsideTheRootIsRefused()
        {
            await db.ResetAsync();
            var (root, _, _, mediaId, _) = await ArrangeReadyRowAsync(db);
            var outsideDir = TestMedia.NewTempDir();
            try
            {
                var subject = await LoadSubjectAsync(db, mediaId);
                var outsidePath = Path.Combine(outsideDir, "forged.mp3");

                // A plan the REAL planner would never build (outsidePath is not under `root`) —
                // minted and read back through the real HMAC codec, proving the EXECUTOR's own
                // re-check catches this, not merely "the planner would have refused it."
                var forgedPlan = new FileActionPlan(
                    mediaId, subject.Xmin, FileActionVerb.Rename, subject.Path, outsidePath, [], Now.AddMinutes(10));
                var tokens = new HmacFileActionPlanTokens(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));
                var token = tokens.Mint(forgedPlan, Now);
                Assert.True(tokens.TryRead(token, Now, out var readBackPlan, out _));

                var executor = BuildExecutor(db, root);
                var outcome = await executor.ExecuteAsync(readBackPlan!, token, CancellationToken.None);

                Assert.Equal((FileActionOutcomeKind.Refused, FileActionRule.OutsideRoot), (outcome.Kind, outcome.Rule));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(outsideDir, recursive: true);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate — a file action and a scan tick never overlap (SPEC F154.6)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGate(DatabaseFixture db)
    {
        [Fact]
        public async Task AScanTickLogsAlreadyInProgressWhileTheGateIsHeld()
        {
            await db.ResetAsync();
            var (gate, probe, firstCall, root, repo, _) = await ArrangeHeldGateAsync(db);
            try
            {
                var capturingLogger = new CapturingLogger<ScanService>();
                var (scan, queue) = Harness.Scanner(repo, root, logger: capturingLogger, gate: gate);

                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Contains(capturingLogger.Informational, m => m.Contains("already in progress", StringComparison.Ordinal));
            }
            finally
            {
                probe.Release();
                await firstCall;
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task ASecondExecutorCallTimesOutToBusy()
        {
            await db.ResetAsync();
            var (gate, probe, firstCall, root, _, plan) = await ArrangeHeldGateAsync(db);
            try
            {
                var executor2 = BuildExecutor(db, root, gate: gate, gateTimeoutSeconds: 1);

                var outcome = await executor2.ExecuteAsync(plan, "tok-gate-2", CancellationToken.None);

                Assert.Equal(FileActionOutcomeKind.Busy, outcome.Kind);
            }
            finally
            {
                probe.Release();
                await firstCall;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>T380 review round 3 — the post-commit budget is now injectable (a trailing,
    /// DI-invisible constructor parameter, the <c>Step3bFailureDetail</c> test-seam precedent), so
    /// this fact can be RED against the round-2 ordering directly instead of merely plausible: a
    /// SUB-SECOND budget, with the gate held LONGER than it. Under the round-2 bug (the post-commit
    /// token built BEFORE the gate wait), a 400ms budget started at time zero would already be dead
    /// by the ~700ms mark, well before the second executor's own post-commit phase even begins —
    /// its Done outcome below is only possible because the fix builds that token AFTER the gate wait
    /// completes, giving it a full, fresh 400ms window starting from THAT point, ample for the fast
    /// rename + DB round trip that follows. Two independent rows, so the second executor's own
    /// binding never depends on whether/when the first one finishes.
    /// <c>ScenarioBusyAudit.ABusyOutcomeWritesAnAuditRow</c> is this fix's OTHER half — the Busy path
    /// itself, after a full gate timeout, still gets a fresh token for its own audit write.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGateWaitNeverStealsFromThePostCommitBudget(DatabaseFixture db)
    {
        [Fact]
        public async Task AGateHeldLongerThanASubSecondBudgetStillCompletesTheDbPhase()
        {
            await db.ResetAsync();
            var (root1, _, _, mediaId1, _) = await ArrangeReadyRowAsync(db, fileName: "first.mp3");
            var (root2, _, _, mediaId2, _) = await ArrangeReadyRowAsync(db, fileName: "second.mp3");
            try
            {
                var gate = new ScanGate();
                var probe = new BlockingFileSystemProbe(new FileSystemProbe());

                var planner1 = BuildPlanner(root1);
                var subject1 = await LoadSubjectAsync(db, mediaId1);
                var plan1 = planner1.Plan(subject1, FileActionVerb.Rename, null, Now).Plan!;
                var firstExecutor = BuildExecutor(db, root1, gate: gate, probe: probe);
                var firstCall = Task.Run(() => firstExecutor.ExecuteAsync(plan1, "tok-hold", CancellationToken.None));
                probe.WaitUntilEntered();

                var planner2 = BuildPlanner(root2);
                var subject2 = await LoadSubjectAsync(db, mediaId2);
                var plan2 = planner2.Plan(subject2, FileActionVerb.Rename, null, Now).Plan!;
                var secondExecutor = BuildExecutor(
                    db, root2, gate: gate, gateTimeoutSeconds: 5, postCommitBudget: TimeSpan.FromMilliseconds(400));
                var secondCall = Task.Run(() => secondExecutor.ExecuteAsync(plan2, "tok-second", CancellationToken.None));

                // Hold the gate for LONGER than the second executor's own 400ms post-commit budget.
                await Task.Delay(TimeSpan.FromMilliseconds(700));
                probe.Release();
                await firstCall;

                var secondOutcome = await secondCall;

                Assert.Equal(FileActionOutcomeKind.Done, secondOutcome.Kind);
            }
            finally
            {
                Directory.Delete(root1, recursive: true);
                Directory.Delete(root2, recursive: true);
            }
        }
    }
}
