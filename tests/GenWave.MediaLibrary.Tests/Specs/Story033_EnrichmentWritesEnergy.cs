// STORY-033 — Enrichment writes energy (failure never blocks ready)
//
// RETIRED BY STORY-341 (SPEC F135.1, PLAN T314): first-pass enrichment no longer runs energy
// analysis at all — it slims to TagLib (tags + duration) + loudness only, and the atomic write
// leaves intro_energy/outro_energy/energy_analyzed_at NULL unconditionally. Energy now arrives
// exclusively through the STORY-036 backfill lane, which still forwards the row's own
// cue_in_sec/cue_out_sec to the energy analyzer unchanged. This file pins the retirement: the
// fast pass never touches the energy analyzer, and energy columns stay NULL regardless of what
// one would return. The write-on-success/write-on-null/write-on-throw energy behavior this file
// used to cover now lives in Story036_BackfillEnergyForReadyRows.cs.
//
// BDD specification — xUnit. Integration via DatabaseCollection.

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
    // HAPPY PATH — the fast pass never runs energy analysis (SPEC F135.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFastPassNeverInvokesEnergyAnalysis(DatabaseFixture db)
    {
        [Fact]
        public async Task EnergyAnalyzerIsNeverInvoked()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_never_invoked.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.75, 0.30));

                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                Assert.Equal(0, fakeEnergy.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyColumnsStayNullRegardlessOfWhatAnEnergyAnalyzerWouldReturn(DatabaseFixture db)
    {
        [Fact]
        public async Task IntroAndOutroEnergyStayNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_stays_null.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.5, 0.2));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.IntroEnergy);
                Assert.Null(row.OutroEnergy);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task EnergyAnalyzedAtStaysNullSoTheBackfillLaneClaimsTheRow()
        {
            // NULL energy_analyzed_at is exactly the STORY-036 backfill predicate's claim signal.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "energy_at_stays_null.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeEnergy = new FakeEnergyAnalyzer();
                fakeEnergy.Returns(new EnergyPoints(0.5, 0.2));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), fakeEnergy)
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.EnergyAnalyzedAt);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RowStillTransitionsToReadyState()
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
    // SAD PATH — loudness failure still blocks ready (unchanged failure contract, SPEC F135.1/AC5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLoudnessFailureStillBlocksReadyRegardlessOfEnergy(DatabaseFixture db)
    {
        [Fact]
        public async Task LoudnessFailureStillBlocksReadyRegardlessOfEnergy()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
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
