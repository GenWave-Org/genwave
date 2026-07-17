// STORY-036 — Backfill energy for existing ready rows (energy_analyzed_at IS NULL)
//
// BDD specification — xUnit. Integration via DatabaseCollection.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBackfillEnergyForReadyRows
{
    // Inline DTO for querying energy backfill-relevant columns directly from Postgres.
    sealed class EnergyBackfillRow
    {
        public string? State { get; set; }
        public double? IntroEnergy { get; set; }
        public double? OutroEnergy { get; set; }
        public double? IntegratedLufs { get; set; }
        public DateTime? EnergyAnalyzedAt { get; set; }
    }

    static async Task<EnergyBackfillRow> SelectRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<EnergyBackfillRow>(
            "select state, intro_energy, outro_energy, integrated_lufs, energy_analyzed_at from library.media where id = @id",
            new { id });
    }

    /// <summary>Seeds a ready row with energy_analyzed_at = NULL (simulating a pre-E8 row).</summary>
    static async Task<(long id, string path)> SeedReadyNoEnergyAsync(DatabaseFixture db, string path)
    {
        var repo = Harness.Repo(db);
        var fi = new FileInfo(path);
        var id = await repo.InsertDiscoveredAsync(path, "flac", fi.Length, Harness.Mtime, CancellationToken.None);
        // WriteEnrichmentAsync with EnergyAnalyzedAt = null simulates a row that pre-dates E8.
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true) with { EnergyAnalyzedAt = null }, CancellationToken.None);
        return (id, path);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBackfillClaimsUnanalyzedReadyRows(DatabaseFixture db)
    {
        [Fact]
        public async Task ReadyRowWithNullEnergyAnalyzedAtGetsEnergyPopulated()
        {
            // Given a ready row, energy_analyzed_at IS NULL; one enricher tick → intro/outro_energy populated.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_backfill_populated.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoEnergyAsync(db, path);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.3, 0.7));

                var svc = Harness.BackfillEnergyWith(repo, fakeEnergy);
                await svc.BackfillEnergyAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal(0.3, row.IntroEnergy);
                Assert.Equal(0.7, row.OutroEnergy);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task BackfillSetsEnergyAnalyzedAt()
        {
            // After backfill the row's energy_analyzed_at is non-null.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_backfill_at.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoEnergyAsync(db, path);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.2, 0.8));

                var svc = Harness.BackfillEnergyWith(repo, fakeEnergy);
                await svc.BackfillEnergyAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.EnergyAnalyzedAt);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task BackfillSkipsLoudnessReMeasurement()
        {
            // integrated_lufs is unchanged — only energy runs in the backfill path.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_backfill_no_lufs.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoEnergyAsync(db, path);

                // Record integrated_lufs before backfill; it must not change.
                var before = await SelectRowAsync(db, id);
                var lufs = before.IntegratedLufs;

                var fakeLoud = new FakeLoudnessAnalyzer();
                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.4, 0.6));

                // Build service with fakeLoud wired as the loudness analyzer — it must not be called.
                var svc = new GenWave.MediaLibrary.Enrich.EnrichmentService(
                    repo,
                    new GenWave.MediaLibrary.Enrich.Enricher(
                        fakeLoud,
                        new FakeCueAnalyzer(),
                        fakeEnergy,
                        new FakeBpmAnalyzer(),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<GenWave.MediaLibrary.Enrich.Enricher>.Instance),
                    System.Threading.Channels.Channel.CreateUnbounded<long>(),
                    new FakeOptionsMonitor<GenWave.MediaLibrary.Options.LibraryOptions>(new GenWave.MediaLibrary.Options.LibraryOptions()),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<GenWave.MediaLibrary.Enrich.EnrichmentService>.Instance,
                    new FakeCueAnalyzer(),
                    Microsoft.Extensions.Options.Options.Create(new GenWave.Loudness.CueDetectionOptions()),
                    fakeEnergy,
                    new FakeBpmAnalyzer(),
                    new FakeYearLookup(),
                    new FakeOptionsMonitor<GenWave.MediaLibrary.Options.YearLookupOptions>(
                        new GenWave.MediaLibrary.Options.YearLookupOptions()));

                await svc.BackfillEnergyAsync(CancellationToken.None);

                // Loudness analyzer was not invoked.
                Assert.Equal(0, fakeLoud.Calls);

                // integrated_lufs is unchanged.
                var after = await SelectRowAsync(db, id);
                Assert.Equal(lufs, after.IntegratedLufs);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task BackfillIsCappedAtFiftyRowsPerTick()
        {
            // Given >50 eligible rows; one tick processes at most 50 (LIMIT 50).
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                const int totalRows = 70;
                const int batchSize = 50;

                for (var i = 0; i < totalRows; i++)
                {
                    var p = TestMedia.CreateTone(dir, $"energy_limit_{i:000}.flac");
                    var fi = new FileInfo(p);
                    var rowId = await repo.InsertDiscoveredAsync(p, "flac", fi.Length, Harness.Mtime, CancellationToken.None);
                    await repo.WriteEnrichmentAsync(rowId, Harness.ReadyResult(true) with { EnergyAnalyzedAt = null }, CancellationToken.None);
                }

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.1, 0.9));

                var svc = Harness.BackfillEnergyWith(repo, fakeEnergy, batchSize);
                await svc.BackfillEnergyAsync(CancellationToken.None);

                Assert.Equal(batchSize, fakeEnergy.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAlreadyAnalyzedRowsAreNotReclaimed(DatabaseFixture db)
    {
        [Fact]
        public async Task RowWithEnergyAnalyzedAtSetIsNotSelected()
        {
            // A row whose energy_analyzed_at is already set is not claimed by the backfill query.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);

                // Seed a row that completed first-pass enrichment with energy_analyzed_at set.
                var path = TestMedia.CreateTone(dir, "energy_skip_done.flac");
                var fi = new FileInfo(path);
                var id = await repo.InsertDiscoveredAsync(path, "flac", fi.Length, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true), CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.5, 0.5));

                var svc = Harness.BackfillEnergyWith(repo, fakeEnergy);
                await svc.BackfillEnergyAsync(CancellationToken.None);

                // fakeEnergy must NOT be invoked — the row already has energy_analyzed_at set.
                Assert.Equal(0, fakeEnergy.Calls);
                _ = id;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task EnergyAnalyzedAtIsSetEvenWhenAnalyzerReturnsNull()
        {
            // energy_analyzed_at is set unconditionally — even when the analyzer returns no result —
            // so the row is not re-picked on the next tick.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_null_result.flac");
                var repo = Harness.Repo(db);
                (var id, _) = await SeedReadyNoEnergyAsync(db, path);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(null);

                var svc = Harness.BackfillEnergyWith(repo, fakeEnergy);
                await svc.BackfillEnergyAsync(CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.EnergyAnalyzedAt);
                Assert.Null(row.IntroEnergy);
                Assert.Null(row.OutroEnergy);

                // Run again — calls must not increase because the row is now excluded.
                var callsAfterFirst = fakeEnergy.Calls;
                await svc.BackfillEnergyAsync(CancellationToken.None);
                Assert.Equal(callsAfterFirst, fakeEnergy.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
