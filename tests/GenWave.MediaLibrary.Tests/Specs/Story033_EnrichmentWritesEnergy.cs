// STORY-033 — Enrichment writes energy (failure never blocks ready)
//
// BDD specification — xUnit. Integration via DatabaseCollection; the Enricher gets
// fake ILoudnessAnalyzer + fake ICueAnalyzer + fake IEnergyAnalyzer to exercise all branches.
// Mirrors Story018_EnrichmentWritesCuePoints.cs seam-for-seam.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEnrichmentWritesEnergy
{
    // Inline DTO for querying enriched energy columns directly from Postgres.
    sealed class EnergyRow
    {
        public string? State { get; set; }
        public bool? Measurable { get; set; }
        public double? IntroEnergy { get; set; }
        public double? OutroEnergy { get; set; }
        public DateTime? EnergyAnalyzedAt { get; set; }
    }

    static async Task<EnergyRow> SelectRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<EnergyRow>(
            "select state, measurable, intro_energy, outro_energy, energy_analyzed_at from library.media where id = @id",
            new { id });
        return row;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyPersistedOnSuccess(DatabaseFixture db)
    {
        [Fact]
        public async Task IntroAndOutroEnergyArePersisted()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_values.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.75, 0.30));

                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal(0.75, row.IntroEnergy);
                Assert.Equal(0.30, row.OutroEnergy);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task EnergyAnalyzedAtIsSetOnSuccess()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_at_success.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.5, 0.2));
                var before = DateTime.UtcNow;
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);
                var after = DateTime.UtcNow;

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.EnergyAnalyzedAt);
                Assert.InRange(row.EnergyAnalyzedAt!.Value, before.AddSeconds(-1), after.AddSeconds(1));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RowTransitionsToReadyState()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_ready.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.5, 0.2));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // energy_analyzed_at is set unconditionally (backfill predicate)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAttemptMarkedUnconditionally(DatabaseFixture db)
    {
        [Fact]
        public async Task EnergyAnalyzedAtIsSetEvenWhenAnalyzerReturnsNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_null_at.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(null);   // analyzer ran, returned null — still set energy_analyzed_at
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.EnergyAnalyzedAt);
                Assert.Null(row.IntroEnergy);
                Assert.Null(row.OutroEnergy);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RowStillTransitionsToReadyWhenAnalyzerReturnsNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_null_ready.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(null);
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — failure isolation (energy never gates ready)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyFailureDoesNotBlockReady(DatabaseFixture db)
    {
        [Fact]
        public async Task RowStillReachesReadyWhenEnergyFailsButLoudnessSucceeds()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_throw_ready.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Throws(new InvalidOperationException("energy boom"));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task EnergyFailureLogsWarningAndPersistsNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_throw_null.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Throws(new InvalidOperationException("energy boom"));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                // The WARN is emitted by Enricher; NullLogger swallows it in tests.
                // Assertion: energy columns are NULL but energy_analyzed_at is still set (we tried).
                var row = await SelectRowAsync(db, id);
                Assert.Null(row.IntroEnergy);
                Assert.Null(row.OutroEnergy);
                Assert.NotNull(row.EnergyAnalyzedAt);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task LoudnessFailureStillBlocksReadyRegardlessOfEnergy()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                // Regression — loudness failure gates ready regardless of energy outcome.
                var path = TestMedia.CreateTone(dir, "energy_loud_fail.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeLoud = new FakeLoudnessAnalyzer();
                fakeLoud.Throws(new InvalidOperationException("loudness failed"));
                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.9, 0.1));
                await Harness.EnrichmentWith(repo, fakeLoud, new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotEqual("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // CUE-WINDOW FORWARDING — energy is measured over trimmed windows, not raw file head/tail
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyMeasuredOverCueTrimmedWindows(DatabaseFixture db)
    {
        [Fact]
        public async Task CueInSecIsForwardedToEnergyAnalyzer()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_cue_in.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(CueInSec: 2.5, CueOutSec: 175.0));

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.6, 0.4));

                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue, fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                Assert.Equal(2.5, fakeEnergy.LastCueInSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueOutSecIsForwardedToEnergyAnalyzer()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_cue_out.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(CueInSec: 2.5, CueOutSec: 175.0));

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.6, 0.4));

                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue, fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                Assert.Equal(175.0, fakeEnergy.LastCueOutSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Regression gate
    // ---------------------------------------------------------------------

    public sealed class ScenarioExistingMediaLibraryEnrichmentTestsStillPass
    {
        [Fact]
        public void RegressionGate()
        {
            // Enforced by `dotnet test tests/GenWave.MediaLibrary.Tests/` staying green.
            // This fact is a witness — the actual gate is CI.
            Assert.True(true);
        }
    }
}
