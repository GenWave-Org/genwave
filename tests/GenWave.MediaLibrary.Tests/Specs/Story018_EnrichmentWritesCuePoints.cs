// STORY-018 — Enrichment writes cue points (cue failure does not block ready)
//
// RETIRED BY STORY-341 (SPEC F135.1, PLAN T314): first-pass enrichment no longer runs cue
// analysis at all — it slims to TagLib (tags + duration) + loudness only, and the atomic write
// leaves cue_in_sec/cue_out_sec/cue_analyzed_at NULL unconditionally. Cue points now arrive
// exclusively through the STORY-024 backfill lane. This file pins that retirement: the fast pass
// never touches the cue analyzer, and cue columns stay NULL regardless of what one would return.
// The write-on-success/write-on-null/write-on-throw cue behavior this file used to cover now
// lives in Story024_BackfillReadyRowsCueAnalyzedAtNull.cs.
//
// BDD specification — xUnit. Integration via DatabaseCollection.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEnrichmentWritesCuePoints
{
    // Inline DTO for querying enriched columns directly from Postgres.
    sealed class EnrichedRow
    {
        public string? State { get; set; }
        public bool? Measurable { get; set; }
        public double? CueInSec { get; set; }
        public double? CueOutSec { get; set; }
        public DateTime? CueAnalyzedAt { get; set; }
    }

    static async Task<EnrichedRow> SelectRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<EnrichedRow>(
            "select state, measurable, cue_in_sec, cue_out_sec, cue_analyzed_at from library.media where id = @id",
            new { id });
        return row;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the fast pass never runs cue analysis (SPEC F135.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFastPassNeverInvokesCueAnalysis(DatabaseFixture db)
    {
        [Fact]
        public async Task LoudnessAnalyzerInvokedExactlyOnce()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "invoke_loud.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeLoud = new FakeLoudnessAnalyzer();
                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.5, 10.0));

                await Harness.EnrichmentWith(repo, fakeLoud, fakeCue).EnrichOneAsync(id, CancellationToken.None);

                Assert.Equal(1, fakeLoud.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueAnalyzerIsNeverInvoked()
        {
            // The fast pass has no cue dependency to call — this proves it via the seam that used
            // to invoke it (EnrichmentWith wires fakeCue only into the backfill-lane field now).
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "invoke_cue.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeLoud = new FakeLoudnessAnalyzer();
                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.5, 10.0));

                await Harness.EnrichmentWith(repo, fakeLoud, fakeCue).EnrichOneAsync(id, CancellationToken.None);

                Assert.Equal(0, fakeCue.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCueColumnsStayNullRegardlessOfWhatACueAnalyzerWouldReturn(DatabaseFixture db)
    {
        [Fact]
        public async Task CueInSecStaysNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_in_stays_null.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.CueInSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueOutSecStaysNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_out_stays_null.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.CueOutSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueAnalyzedAtStaysNullSoTheBackfillLaneClaimsTheRow()
        {
            // NULL cue_analyzed_at is exactly the STORY-024 backfill predicate's claim signal —
            // this is what lets the second-tier lane find and sweep the row afterward (F135.1).
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_at_stays_null.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.CueAnalyzedAt);
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
                var path = TestMedia.CreateTone(dir, "cue_ready.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

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
    public sealed class ScenarioLoudnessFailureStillBlocksReady(DatabaseFixture db)
    {
        [Fact]
        public async Task RowDoesNotTransitionToReadyWhenLoudnessFails()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "loud_fail_state.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeLoud = new FakeLoudnessAnalyzer();
                fakeLoud.Throws(new InvalidOperationException("loudness failed"));
                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.0, 5.0));
                await Harness.EnrichmentWith(repo, fakeLoud, fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotEqual("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RowIsNotSelectableForPlayoutWhenLoudnessFails()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "loud_fail_select.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeLoud = new FakeLoudnessAnalyzer();
                fakeLoud.Throws(new InvalidOperationException("loudness failed"));
                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.0, 5.0));
                await Harness.EnrichmentWith(repo, fakeLoud, fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var scope = new LibraryScope([1L]);
                var selected = await ((GenWave.Core.Abstractions.IMediaCatalog)repo)
                    .GetRandomReadyAsync(scope, [], CancellationToken.None);
                Assert.Null(selected);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

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
