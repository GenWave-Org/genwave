// STORY-111 — Never-play suppresses every selection path (WIRE)
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection —
// the LEFT JOIN + NOT COALESCE predicate is selection SQL, provable only against the
// real planner. S3 lands the predicate in GetRandomReadyAsync and the status counts.
// See docs/PLAN.md Epic S.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureNeverPlaySelectionSuppression
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static MediaRatingRepository RatingRepo(DatabaseFixture db) => new(db.DataSource);

    /// <summary>Inserts a ready + measurable + eligible row in library 1 — a selectable row.</summary>
    static async Task<long> InsertSelectableRowAsync(DatabaseFixture db, string path)
    {
        var repo = Harness.Repo(db);
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);
        return id;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — one predicate, every path
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAFlaggedRowIsNeverSelected(DatabaseFixture db)
    {
        [Fact]
        public async Task GetRandomReadyNeverReturnsTheFlaggedRow()
        {
            // Arrange: two ready+measurable+eligible rows in scope, one flagged never_play.
            await db.ResetAsync();
            var flaggedId = await InsertSelectableRowAsync(db, "/never-play/flagged.flac");
            var openId = await InsertSelectableRowAsync(db, "/never-play/open.flac");
            await RatingRepo(db).SetNeverPlayAsync(flaggedId.ToString(), true, CancellationToken.None);

            var repo = Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            // Act: GetRandomReadyAsync many times (>=50). Assert: the flagged id never appears (F33.6).
            for (var i = 0; i < 50; i++)
            {
                var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);
                Assert.NotNull(reference);
                Assert.Equal(openId.ToString(), reference.MediaId);
            }
        }

        [Fact]
        public async Task TheOnlyRowFlaggedMeansSelectionReturnsNull()
        {
            // Arrange: a scope whose single ready row is flagged. Act: select.
            await db.ResetAsync();
            var id = await InsertSelectableRowAsync(db, "/never-play/only-row-flagged.flac");
            await RatingRepo(db).SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);

            var repo = Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            // Assert: null — the F6.3 empty-pool contract, feeding F18.5/F25 degradation (F33.7).
            var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);
            Assert.Null(reference);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnflaggingRestoresSelectability(DatabaseFixture db)
    {
        [Fact]
        public async Task TheRowIsSelectableAgainAfterUnflagging()
        {
            // Flag -> confirm excluded -> unflag -> the row is returned again (F33.6).
            await db.ResetAsync();
            var id = await InsertSelectableRowAsync(db, "/never-play/unflag-restores.flac");
            var ratingRepo = RatingRepo(db);
            var repo = Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            await ratingRepo.SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);
            Assert.Null(await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None));

            await ratingRepo.SetNeverPlayAsync(id.ToString(), false, CancellationToken.None);
            var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Equal(id.ToString(), reference.MediaId);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPlayableCountsHonorTheFlag(DatabaseFixture db)
    {
        [Fact]
        public async Task AnAllFlaggedScopeReportsZeroPlayable()
        {
            // GetStatusCountsAsync's playable/safeScope.playable applies the same predicate,
            // so the F31.4/F31.5 depleted warnings fire truthfully (F33.6).
            await db.ResetAsync();
            var firstId = await InsertSelectableRowAsync(db, "/never-play/all-flagged-1.flac");
            var secondId = await InsertSelectableRowAsync(db, "/never-play/all-flagged-2.flac");
            var ratingRepo = RatingRepo(db);
            await ratingRepo.SetNeverPlayAsync(firstId.ToString(), true, CancellationToken.None);
            await ratingRepo.SetNeverPlayAsync(secondId.ToString(), true, CancellationToken.None);

            var repo = Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var counts = await repo.GetStatusCountsAsync(scope, CancellationToken.None);

            Assert.Equal(0, counts.Playable);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the ledger stays a ledger
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioScoreNeverEntersSelection(DatabaseFixture db)
    {
        [Fact]
        public async Task DivergentScoresDoNotSkewSelection()
        {
            // Arrange: two selectable rows, scores 1 and 100. Act: select many times (>=200).
            await db.ResetAsync();
            var lowScoreId = await InsertSelectableRowAsync(db, "/never-play/score-low.flac");
            var highScoreId = await InsertSelectableRowAsync(db, "/never-play/score-high.flac");

            await using (var conn = await db.DataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync(
                    "insert into library.media_rating (media_id, score) values (@id, 1)", new { id = lowScoreId });
                await conn.ExecuteAsync(
                    "insert into library.media_rating (media_id, score) values (@id, 100)", new { id = highScoreId });
            }

            var repo = Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var seenIds = new HashSet<string>();
            for (var i = 0; i < 200; i++)
            {
                var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);
                Assert.NotNull(reference);
                seenIds.Add(reference.MediaId);
            }

            // Assert (single): both ids appear - score is not a selection term this phase (F33.8).
            // The uniform-distribution spot-check beyond presence is S8's live gate, not this spec.
            Assert.True(
                seenIds.Contains(lowScoreId.ToString()) && seenIds.Contains(highScoreId.ToString()),
                $"expected both {lowScoreId} (score 1) and {highScoreId} (score 100) to appear over 200 draws; saw {string.Join(',', seenIds)}");
        }
    }
}
