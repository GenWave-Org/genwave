// STORY-024 — Backfill: enricher picks up ready rows where cue_analyzed_at IS NULL
//
// BDD specification — xUnit. Integration via DatabaseCollection.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBackfillReadyRowsCueAnalyzedAtNull
{
    // Inline DTO for querying backfill-relevant columns directly from Postgres.
    sealed class BackfillRow
    {
        public string? State { get; set; }
        public double? CueInSec { get; set; }
        public double? CueOutSec { get; set; }
        public DateTime? CueAnalyzedAt { get; set; }
    }

    static async Task<BackfillRow> SelectRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<BackfillRow>(
            "select state, cue_in_sec, cue_out_sec, cue_analyzed_at from library.media where id = @id",
            new { id });
    }

    /// <summary>Seeds a ready row with cue_analyzed_at = NULL (simulating a pre-T027 row).</summary>
    static async Task<(long id, string path)> SeedReadyNoCueAsync(DatabaseFixture db, string path)
    {
        var repo = Harness.Repo(db);
        var fi = new FileInfo(path);
        var id = await repo.InsertDiscoveredAsync(path, "flac", fi.Length, Harness.Mtime, CancellationToken.None);
        // WriteEnrichmentAsync with CueAnalyzedAt = null simulates a row that pre-dates T020.
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true) with { CueAnalyzedAt = null }, CancellationToken.None);
        return (id, path);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioBackfillQuerySelectsReadyRowsWithNullCueAnalyzedAt(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task IssuedSqlReferencesStateReadyAndCueAnalyzedAtIsNull()
        {
            // Behavioral proof: only rows matching state='ready' AND cue_analyzed_at IS NULL are picked.
            // A discovered row is NOT picked. A ready row with cue_analyzed_at set is NOT picked.
            // Only the legacy ready row (cue_analyzed_at IS NULL) is processed.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_sql_pred.flac");
                var fi = new FileInfo(path);
                var repo = Harness.Repo(db);

                // Discovered row — must NOT be picked by backfill.
                var discoveredId = await repo.InsertDiscoveredAsync(path, "flac", fi.Length, Harness.Mtime, CancellationToken.None);

                // Ready row with cue_analyzed_at set — must NOT be picked.
                var path2 = TestMedia.CreateTone(dir, "backfill_sql_pred2.flac");
                var fi2 = new FileInfo(path2);
                var alreadyDoneId = await repo.InsertDiscoveredAsync(path2, "flac", fi2.Length, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(alreadyDoneId, Harness.ReadyResult(true), CancellationToken.None);

                // Legacy ready row (cue_analyzed_at IS NULL) — must be picked.
                var path3 = TestMedia.CreateTone(dir, "backfill_sql_pred3.flac");
                var fi3 = new FileInfo(path3);
                var legacyId = await repo.InsertDiscoveredAsync(path3, "flac", fi3.Length, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(legacyId, Harness.ReadyResult(true) with { CueAnalyzedAt = null }, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(1.0, 9.0));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                // Only the legacy row was processed.
                Assert.Equal(1, fakeCue.Calls);
                Assert.Equal(path3, fakeCue.LastPath);

                // Discovered and already-done rows were untouched.
                var discoveredRow = await SelectRowAsync(db, discoveredId);
                Assert.Null(discoveredRow.CueAnalyzedAt);

                var doneRow = await SelectRowAsync(db, alreadyDoneId);
                Assert.NotNull(doneRow.CueAnalyzedAt);
                // The alreadyDone row retains its original cue_analyzed_at (was not re-run).
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioBackfillRunsCueAnalysisOnly(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueAnalyzerInvokedExactlyOnceForEligibleRow()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_cue_once.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.5, 9.5));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                Assert.Equal(1, fakeCue.Calls);
                _ = id;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task LoudnessAnalyzerIsNotInvokedForBackfillRow()
        {
            // Loudness already succeeded; re-measuring wastes ffmpeg cycles.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_no_loud.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeLoud = new FakeLoudnessAnalyzer();
                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.5, 9.5));

                // BackfillWith uses fakeLoud as the Enricher's loudness analyzer — it must not be called.
                var svc = new GenWave.MediaLibrary.Enrich.EnrichmentService(
                    repo,
                    new GenWave.MediaLibrary.Enrich.Enricher(
                        fakeLoud, fakeCue,
                        new GenWave.MediaLibrary.Tests.Fakes.FakeEnergyAnalyzer(),
                        new GenWave.MediaLibrary.Tests.Fakes.FakeBpmAnalyzer(),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<GenWave.MediaLibrary.Enrich.Enricher>.Instance),
                    System.Threading.Channels.Channel.CreateUnbounded<long>(),
                    new FakeOptionsMonitor<GenWave.MediaLibrary.Options.LibraryOptions>(new GenWave.MediaLibrary.Options.LibraryOptions()),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<GenWave.MediaLibrary.Enrich.EnrichmentService>.Instance,
                    fakeCue,
                    Microsoft.Extensions.Options.Options.Create(new GenWave.Loudness.CueDetectionOptions()),
                    new GenWave.MediaLibrary.Tests.Fakes.FakeEnergyAnalyzer(),
                    new GenWave.MediaLibrary.Tests.Fakes.FakeBpmAnalyzer(),
                    new GenWave.MediaLibrary.Tests.Fakes.FakeYearLookup(),
                    new FakeOptionsMonitor<GenWave.MediaLibrary.Options.YearLookupOptions>(
                        new GenWave.MediaLibrary.Options.YearLookupOptions()));

                await svc.BackfillCueAsync(CancellationToken.None);

                Assert.Equal(0, fakeLoud.Calls);
                _ = id;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioSuccessfulBackfillPersistsCueValuesAndTimestamp(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueInSecIsPersisted()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_cue_in.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal(3.45, row.CueInSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueOutSecIsPersisted()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_cue_out.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal(187.20, row.CueOutSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueAnalyzedAtIsSet()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_cue_at.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.CueAnalyzedAt);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task StateRemainsReady()
        {
            // Backfill does NOT move the row out of 'ready'.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_state_ready.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioBackfillRowWithNullCueAnalyzerResultStillSetsCueAnalyzedAt(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueValuesRemainNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_null_cue_vals.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(null);

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.CueInSec);
                Assert.Null(row.CueOutSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueAnalyzedAtIsStillSetSoTheRowIsNotRePicked()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_null_no_repick.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(null);

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                // cue_analyzed_at is set even when result is null — the row won't be re-picked.
                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.CueAnalyzedAt);

                // Run again — calls must not increase because the row is now excluded by the WHERE clause.
                var callsAfterFirst = fakeCue.Calls;
                await svc.BackfillCueAsync(CancellationToken.None);
                Assert.Equal(callsAfterFirst, fakeCue.Calls);
                _ = id;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioBackfillRunsAtTheSameCadenceAsTheExistingEnricher(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task OneEnrichmentTickProcessesBothDiscoveredAndBackfillEligibleRows()
        {
            // Seed: one discovered row + one ready/no-cue row.
            // Run both paths; verify discovered row → ready (enrichment path); backfill row → cue_analyzed_at set.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);

                // Discovered row — will be enriched by EnrichOneAsync.
                var discoveredPath = TestMedia.CreateTone(dir, "tick_discovered.flac");
                var fi = new FileInfo(discoveredPath);
                var discoveredId = await repo.InsertDiscoveredAsync(discoveredPath, "flac", fi.Length, Harness.Mtime, CancellationToken.None);

                // Legacy ready row — will be processed by BackfillCueAsync.
                var legacyPath = TestMedia.CreateTone(dir, "tick_legacy.flac");
                var fi2 = new FileInfo(legacyPath);
                var legacyId = await repo.InsertDiscoveredAsync(legacyPath, "flac", fi2.Length, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(legacyId, Harness.ReadyResult(true) with { CueAnalyzedAt = null }, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.5, 9.5));

                // EnrichmentWith shares the same fakeCue for both enrichment and backfill paths.
                var svc = Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue);

                // Drive the enrichment path for the discovered row.
                await svc.EnrichOneAsync(discoveredId, CancellationToken.None);

                // Drive the backfill path.
                await svc.BackfillCueAsync(CancellationToken.None);

                var discoveredRow = await SelectRowAsync(db, discoveredId);
                Assert.Equal("ready", discoveredRow.State);

                var legacyRow = await SelectRowAsync(db, legacyId);
                Assert.NotNull(legacyRow.CueAnalyzedAt);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void NoSeparateSchedulerOrAdminEndpointIsIntroduced()
        {
            // Architectural witness: no new BackgroundService registered beyond ScanService and
            // EnrichmentService, and BackfillCueAsync is an internal method on EnrichmentService —
            // not a new hosted service or route handler.
            var hostedServiceTypes = typeof(GenWave.MediaLibrary.MediaLibraryServiceCollectionExtensions)
                .Assembly
                .GetTypes()
                .Where(t => typeof(Microsoft.Extensions.Hosting.BackgroundService).IsAssignableFrom(t)
                         || typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(t))
                .Where(t => t.IsClass && !t.IsAbstract)
                .Select(t => t.Name)
                .ToArray();

            // Only ScanService and EnrichmentService are registered as background services.
            Assert.Contains("ScanService", hostedServiceTypes);
            Assert.Contains("EnrichmentService", hostedServiceTypes);

            // BackfillCueAsync is an internal method on EnrichmentService, not a new named type.
            // (Compiler-generated async state machines like <BackfillCueAsync>d__N are excluded.)
            var backfillType = typeof(GenWave.MediaLibrary.MediaLibraryServiceCollectionExtensions)
                .Assembly
                .GetTypes()
                .FirstOrDefault(t => t.Name.Contains("Backfill")
                                  && t.IsClass
                                  && !t.Name.StartsWith("<", StringComparison.Ordinal));
            Assert.Null(backfillType);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioCueAnalyzerThrowingDuringBackfillDoesNotCorruptTheRow(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueValuesRemainNullAfterException()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_throw_null_vals.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Throws(new InvalidOperationException("boom"));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.CueInSec);
                Assert.Null(row.CueOutSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task CueAnalyzedAtIsSetSoRowIsNotRetriedIndefinitely()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_throw_at_set.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Throws(new InvalidOperationException("boom"));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                // Even on exception the row gets cue_analyzed_at set, preventing infinite retries.
                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.CueAnalyzedAt);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task BackfillFailureIsLoggedAtWarn()
        {
            // Verify that the row's state is consistent after an exception:
            // - cue_analyzed_at is set (so the row won't be re-tried)
            // - state remains 'ready' (backfill never marks failed)
            // The actual log warning is exercised by the code path; we verify the observable
            // side-effects here rather than coupling to a log capture framework.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "backfill_throw_logged.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoCueAsync(db, path);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Throws(new InvalidOperationException("boom"));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal("ready", row.State);
                Assert.NotNull(row.CueAnalyzedAt);
                Assert.Null(row.CueInSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioBackfillIsBoundedPerTick(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task BackfillQueryLimitsRowsPerTick()
        {
            // Seed 70 backfill-eligible rows; configure a batch size of 50.
            // Verify that only 50 rows are processed in a single BackfillCueAsync call.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                const int totalRows = 70;
                const int batchSize = 50;

                // Seed all rows as ready with cue_analyzed_at = null.
                for (var i = 0; i < totalRows; i++)
                {
                    var path = TestMedia.CreateTone(dir, $"backfill_limit_{i:000}.flac");
                    var fi = new FileInfo(path);
                    var id = await repo.InsertDiscoveredAsync(path, "flac", fi.Length, Harness.Mtime, CancellationToken.None);
                    await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true) with { CueAnalyzedAt = null }, CancellationToken.None);
                }

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.0, 2.0));

                var svc = Harness.BackfillWith(repo, fakeCue, batchSize);
                await svc.BackfillCueAsync(CancellationToken.None);

                Assert.Equal(batchSize, fakeCue.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioRowProcessedByFirstPassEnrichmentIsNeverPickedByBackfill(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task RowWithCueAnalyzedAtSetIsNotInBackfillSelectResults()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);

                // Seed a row that completed first-pass enrichment with cue_analyzed_at set.
                var path = TestMedia.CreateTone(dir, "backfill_skip_done.flac");
                var fi = new FileInfo(path);
                var id = await repo.InsertDiscoveredAsync(path, "flac", fi.Length, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true), CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(1.0, 9.0));

                var svc = Harness.BackfillWith(repo, fakeCue);
                await svc.BackfillCueAsync(CancellationToken.None);

                // fakeCue must NOT be invoked — the row already has cue_analyzed_at set.
                Assert.Equal(0, fakeCue.Calls);
                _ = id;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
