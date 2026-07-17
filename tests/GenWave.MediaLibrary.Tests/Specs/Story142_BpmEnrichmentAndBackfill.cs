// STORY-142 — BPM measured at enrichment (Epic X / SPEC F46.2–F46.4, closes gitea-#190) —
// schema + enrichment + backfill half. The Core contract lives in
// Core.Tests/Specs/Story142_BpmAnalyzerContract.cs; the aubio parse half in
// Specs/Story142_AubioBpmAnalyzer.cs (this project).
//
// BDD specification — xUnit (real Postgres via the DatabaseFixture). Authored PENDING at /plan
// time (2026-07-14, house rule since Epic S).

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBpmEnrichmentAndBackfill
{
    static readonly LibraryScope Scope = new([1L]);

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // Inline DTO for querying BPM-relevant columns directly from Postgres — mirrors the
    // EnergyRow/EnergyBackfillRow helpers in Story033/Story036.
    sealed class BpmRow
    {
        public string? State { get; set; }
        public double? Bpm { get; set; }
        public DateTime? BpmAnalyzedAt { get; set; }
    }

    static async Task<BpmRow> SelectBpmRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<BpmRow>(
            "select state, bpm, bpm_analyzed_at from library.media where id = @id",
            new { id });
    }

    /// <summary>
    /// Returns (data_type, is_nullable) for the named column on library.media, or null when the
    /// column does not exist.
    /// </summary>
    static async Task<(string DataType, string IsNullable)?> QueryColumnAsync(DatabaseFixture db, string columnName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            @"SELECT data_type, is_nullable
              FROM information_schema.columns
              WHERE table_schema = 'library'
                AND table_name   = 'media'
                AND column_name  = @col",
            new { col = columnName });

        return row == default ? null : (row.data_type, row.is_nullable);
    }

    /// <summary>
    /// Executes the real <c>db/10-enrichment2-migration.sh</c> against the test database by piping
    /// the script file to <c>bash -s</c> inside the compose testdb container via the fixture.
    /// </summary>
    static void RunMigrationScript(DatabaseFixture db)
    {
        var scriptPath = Path.Combine(db.RepoRoot, "db", "10-enrichment2-migration.sh");
        db.RunFileInContainer(scriptPath);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFirstPassEnrichmentMeasuresBpm(DatabaseFixture db)
    {
        [Fact]
        public async Task TheMigrationAddsBpmColumnsIdempotently()
        {
            // Simulate a pre-Epic-X DB: drop bpm + bpm_analyzed_at (already present via
            // 01-library.sh's converged fresh-install shape), then prove the migration script both
            // adds them back and tolerates a second run without error or shape drift (F46.2).
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "ALTER TABLE library.media DROP COLUMN IF EXISTS bpm, DROP COLUMN IF EXISTS bpm_analyzed_at");

            Assert.Null(await QueryColumnAsync(db, "bpm"));
            Assert.Null(await QueryColumnAsync(db, "bpm_analyzed_at"));

            RunMigrationScript(db);

            var bpm = await QueryColumnAsync(db, "bpm");
            Assert.NotNull(bpm);
            Assert.Equal("double precision", bpm.Value.DataType);
            Assert.Equal("YES", bpm.Value.IsNullable);

            var bpmAnalyzedAt = await QueryColumnAsync(db, "bpm_analyzed_at");
            Assert.NotNull(bpmAnalyzedAt);
            Assert.Equal("timestamp with time zone", bpmAnalyzedAt.Value.DataType);
            Assert.Equal("YES", bpmAnalyzedAt.Value.IsNullable);

            // Second run — must also succeed without error and leave the shape unchanged.
            RunMigrationScript(db);

            var bpmAfter = await QueryColumnAsync(db, "bpm");
            Assert.NotNull(bpmAfter);
            Assert.Equal(bpm.Value.DataType, bpmAfter.Value.DataType);
            Assert.Equal(bpm.Value.IsNullable, bpmAfter.Value.IsNullable);
        }

        [Fact]
        public async Task EnrichmentWritesBpmAndStampsTheSentinel()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "bpm_first_pass.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeBpm = new FakeBpmAnalyzer();
                fakeBpm.Returns(128.0);

                await Harness.EnrichmentWith(
                        repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), fakeBpm)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectBpmRowAsync(db, id);
                Assert.Equal(128.0, row.Bpm);
                Assert.NotNull(row.BpmAnalyzedAt);
                Assert.Equal("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // BACKFILL — claim shape mirrors cue/energy exactly (F46.3); reenrich reclaims (F46.4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheExistingCatalogBackfills(DatabaseFixture db)
    {
        [Fact]
        public async Task ReadyRowsWithoutTheSentinelAreClaimedAtTheBatchCap()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                const int totalRows = 70;
                const int batchSize = 50;

                for (var i = 0; i < totalRows; i++)
                {
                    var p = TestMedia.CreateTone(dir, $"bpm_limit_{i:000}.flac");
                    var fi = new FileInfo(p);
                    var rowId = await repo.InsertDiscoveredAsync(p, "flac", fi.Length, Harness.Mtime, CancellationToken.None);
                    await repo.WriteEnrichmentAsync(
                        rowId, Harness.ReadyResult(true) with { BpmAnalyzedAt = null }, CancellationToken.None);
                }

                var fakeBpm = new FakeBpmAnalyzer();
                fakeBpm.Returns(100.0);

                var svc = Harness.BackfillBpmWith(repo, fakeBpm, batchSize);
                await svc.BackfillBpmAsync(CancellationToken.None);

                Assert.Equal(batchSize, fakeBpm.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ABpmReenrichResetMakesTheRowClaimableAgain()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "bpm_reenrich_reclaim.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(
                    id, Harness.ReadyResult(true) with { Bpm = 90.0, BpmAnalyzedAt = DateTime.UtcNow },
                    CancellationToken.None);

                var before = await SelectBpmRowAsync(db, id);
                Assert.Equal(90.0, before.Bpm);
                Assert.NotNull(before.BpmAnalyzedAt);

                var result = await ((IAdminMediaReenrichment)repo).ScheduleAsync(
                    id.ToString(), ReenrichFields.Bpm, Scope, CancellationToken.None);
                Assert.Equal(ReenrichResult.Scheduled, result);

                // Reset nulls value + sentinel; state is unchanged (F46.4) — unlike Loudness/Tags,
                // a Bpm reset never drops the row to 'discovered'.
                var afterReset = await SelectBpmRowAsync(db, id);
                Assert.Null(afterReset.Bpm);
                Assert.Null(afterReset.BpmAnalyzedAt);
                Assert.Equal("ready", afterReset.State);

                // The polling backfill predicate (state='ready' AND bpm_analyzed_at IS NULL)
                // reclaims the row on its next tick — no enqueue needed.
                var fakeBpm = new FakeBpmAnalyzer();
                fakeBpm.Returns(140.0);
                var svc = Harness.BackfillBpmWith(repo, fakeBpm);
                await svc.BackfillBpmAsync(CancellationToken.None);

                var afterBackfill = await SelectBpmRowAsync(db, id);
                Assert.Equal(140.0, afterBackfill.Bpm);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAttemptedNoneFoundIsNotAnError(DatabaseFixture db)
    {
        [Fact]
        public async Task ANullAnalyzerResultStampsTheSentinelAndLeavesStateAlone()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "bpm_null_result.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeBpm = new FakeBpmAnalyzer();
                fakeBpm.Returns(null);   // indeterminate tempo — attempted, none found

                await Harness.EnrichmentWith(
                        repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), fakeBpm)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectBpmRowAsync(db, id);
                Assert.Null(row.Bpm);
                Assert.NotNull(row.BpmAnalyzedAt);
                Assert.Equal("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task AStampedNoneFoundRowIsNotReclaimed()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "bpm_none_found_not_reclaimed.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);
                // Seed a row that pre-dates BPM analysis (bpm_analyzed_at IS NULL).
                await repo.WriteEnrichmentAsync(
                    id, Harness.ReadyResult(true) with { BpmAnalyzedAt = null }, CancellationToken.None);

                var fakeBpm = new FakeBpmAnalyzer();
                fakeBpm.Returns(null);

                var svc = Harness.BackfillBpmWith(repo, fakeBpm);
                await svc.BackfillBpmAsync(CancellationToken.None);

                var row = await SelectBpmRowAsync(db, id);
                Assert.Null(row.Bpm);
                Assert.NotNull(row.BpmAnalyzedAt);

                // Run again — calls must not increase because the row is now excluded.
                var callsAfterFirst = fakeBpm.Calls;
                await svc.BackfillBpmAsync(CancellationToken.None);
                Assert.Equal(callsAfterFirst, fakeBpm.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
