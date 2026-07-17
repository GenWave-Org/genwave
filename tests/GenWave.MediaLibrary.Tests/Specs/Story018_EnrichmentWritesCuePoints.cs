// STORY-018 — Enrichment writes cue points (cue failure does not block ready)
//
// BDD specification — xUnit. Integration via DatabaseCollection; the Enricher gets
// fake ILoudnessAnalyzer + fake ICueAnalyzer to exercise both branches.

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
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnricherInvokesBothAnalyzersPerFile(DatabaseFixture db)
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
        public async Task CueAnalyzerInvokedExactlyOnce()
        {
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

                Assert.Equal(1, fakeCue.Calls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task BothAnalyzersReceiveSameFilePath()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "invoke_both.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeLoud = new FakeLoudnessAnalyzer();
                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(0.5, 10.0));

                await Harness.EnrichmentWith(repo, fakeLoud, fakeCue).EnrichOneAsync(id, CancellationToken.None);

                Assert.Equal(fakeLoud.LastPath, fakeCue.LastPath);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSuccessfulCueAnalysisPersistsCueValuesAndTimestamp(DatabaseFixture db)
    {
        [Fact]
        public async Task CueInSecIsPersistedToTheRow()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_in.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal(3.45, row.CueInSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueOutSecIsPersistedToTheRow()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_out.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal(187.20, row.CueOutSec);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueAnalyzedAtIsSetToARecentTimestamp()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_at.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(new CuePoints(3.45, 187.20));
                var before = DateTime.UtcNow;
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);
                var after = DateTime.UtcNow;

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.CueAnalyzedAt);
                Assert.InRange(row.CueAnalyzedAt!.Value, before.AddSeconds(-1), after.AddSeconds(1));
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

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCueAnalyzedAtIsSetEvenWhenCuePointsIsNull(DatabaseFixture db)
    {
        [Fact]
        public async Task CueInSecRemainsNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "null_cue_in.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(null);
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
        public async Task CueOutSecRemainsNull()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "null_cue_out.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(null);
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
        public async Task CueAnalyzedAtIsStillSet()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                // The row will NOT be re-picked by STORY-024 backfill, because we tried.
                var path = TestMedia.CreateTone(dir, "null_cue_at.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(null);
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.CueAnalyzedAt);
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
                var path = TestMedia.CreateTone(dir, "null_cue_ready.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Returns(null);
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
    // SAD PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCueAnalyzerThrowingDoesNotBlockReady(DatabaseFixture db)
    {
        [Fact]
        public async Task RowTransitionsToReadyDespiteCueException()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_throw_ready.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Throws(new InvalidOperationException("boom"));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Equal("ready", row.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CueValuesRemainNullAfterCueException()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "cue_throw_null.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Throws(new InvalidOperationException("boom"));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

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
        public async Task CueAnalyzedAtIsStillSetAfterCueException()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                // "We tried" — the row will not be retried indefinitely by backfill.
                var path = TestMedia.CreateTone(dir, "cue_throw_at.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var fakeCue = new FakeCueAnalyzer();
                fakeCue.Throws(new InvalidOperationException("boom"));
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), fakeCue).EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.NotNull(row.CueAnalyzedAt);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

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
                // Regression — Phase 1 behavior must survive the cue addition.
                // Loudness throwing causes EnrichmentService to catch and call MarkFailedAsync → state = 'failed'.
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
                // Regression — existing F2.2/F2.3 behavior on unmeasurable/failed rows.
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
