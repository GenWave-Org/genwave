// STORY-158 — Rank artists and albums from the Catalog (Epic Z / SPEC F61, closes gitea-#233) —
// the repository half: bulk vote + bulk never-play over the shared bulk filter.
//
// BDD specification — xUnit (real Postgres, MediaLibrary.Tests idiom). Matching ids resolve
// through BuildAdminWhere (exact fields included, effective-scope rules per F23.3); per-row
// semantics are exactly F33 (±1 clamped [0,100], row created at 50; idempotent never-play);
// writes touch library.media_rating ONLY — library.media.xmin never bumps (F61.2, F33.1).
// Mirrors Story110_RatingPersistence (per-row) and Story148_FacetsAndExactFilterSql /
// StoryF3_BulkEligibilityByFilter (shared WHERE builder, scope) idioms.

using GenWave.MediaLibrary.Tests.Fakes;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBulkRatingSql
{
    static MediaRatingRepository RatingRepo(DatabaseFixture db) => new(db.DataSource, new FakeSafeScopeProvider());

    static async Task<int> CountRatingRowsAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from library.media_rating where media_id = @id", new { id });
    }

    static async Task SeedRatingAsync(DatabaseFixture db, long id, int score)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rating (media_id, score) values (@id, @score)", new { id, score });
    }

    static async Task<string> ReadMediaXminAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string>(
            "select xmin::text from library.media where id = @id", new { id }) ?? "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — per-row F33 semantics, in bulk
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBulkVoteAppliesF33SemanticsPerRow(DatabaseFixture db)
    {
        [Fact]
        public async Task AnUpVoteIncrementsEveryMatchingRowByOne()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/bulk-vote-inc-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResultWith(genre: "BulkVoteGenre"), CancellationToken.None);
            var id2 = await repo.InsertDiscoveredAsync("/media/bulk-vote-inc-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResultWith(genre: "BulkVoteGenre"), CancellationToken.None);
            await SeedRatingAsync(db, id1, 60);
            await SeedRatingAsync(db, id2, 60);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(GenresExact: ["BulkVoteGenre"]);

            await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, scope, CancellationToken.None);

            var ratings = await ratingRepo.GetRatingsAsync([id1.ToString(), id2.ToString()], CancellationToken.None);
            Assert.All(ratings, r => Assert.Equal(61, r.Score));
        }

        [Fact]
        public async Task AnUnratedRowIsCreatedAtFiftyBeforeTheVoteApplies()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            // No library.media_rating row is ever written for this id before the sweep (F33.2).
            var id = await repo.InsertDiscoveredAsync("/media/bulk-vote-unrated.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "UnratedBulkArtist"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "UnratedBulkArtist");

            await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, scope, CancellationToken.None);

            var rating = Assert.Single(await ratingRepo.GetRatingsAsync([id.ToString()], CancellationToken.None));
            Assert.Equal(51, rating.Score);
        }

        [Fact]
        public async Task ScoresClampAtOneHundred()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id = await repo.InsertDiscoveredAsync("/media/bulk-vote-clamp-top.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "ClampTopBulkArtist"), CancellationToken.None);
            await SeedRatingAsync(db, id, 100);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "ClampTopBulkArtist");

            await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, scope, CancellationToken.None);

            var rating = Assert.Single(await ratingRepo.GetRatingsAsync([id.ToString()], CancellationToken.None));
            Assert.Equal(100, rating.Score);
        }

        [Fact]
        public async Task ScoresClampAtZero()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id = await repo.InsertDiscoveredAsync("/media/bulk-vote-clamp-bottom.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "ClampBottomBulkArtist"), CancellationToken.None);
            await SeedRatingAsync(db, id, 0);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "ClampBottomBulkArtist");

            await ratingRepo.BulkVoteAsync(filter, VoteDirection.Down, scope, CancellationToken.None);

            var rating = Assert.Single(await ratingRepo.GetRatingsAsync([id.ToString()], CancellationToken.None));
            Assert.Equal(0, rating.Score);
        }

        [Fact]
        public async Task TheAffectedCountMatchesTheMatchedRowSet()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            for (var i = 0; i < 3; i++)
            {
                var id = await repo.InsertDiscoveredAsync($"/media/bulk-vote-count-{i}.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(genre: "BulkVoteCountGenre"), CancellationToken.None);
            }
            var otherId = await repo.InsertDiscoveredAsync("/media/bulk-vote-count-other.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(otherId, Harness.ReadyResultWith(genre: "OtherGenre"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(GenresExact: ["BulkVoteCountGenre"]);

            var affected = await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, scope, CancellationToken.None);

            Assert.Equal(3, affected);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBulkNeverPlaySetsAndClears(DatabaseFixture db)
    {
        [Fact]
        public async Task NeverPlayTrueSetsEveryMatchingRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/bulk-never-play-set-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResultWith(genre: "BulkNeverPlayGenre"), CancellationToken.None);
            var id2 = await repo.InsertDiscoveredAsync("/media/bulk-never-play-set-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResultWith(genre: "BulkNeverPlayGenre"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(GenresExact: ["BulkNeverPlayGenre"]);

            await ratingRepo.BulkSetNeverPlayAsync(filter, true, scope, CancellationToken.None);

            var ratings = await ratingRepo.GetRatingsAsync([id1.ToString(), id2.ToString()], CancellationToken.None);
            Assert.All(ratings, r => Assert.True(r.NeverPlay));
        }

        [Fact]
        public async Task ARepeatSetIsIdempotent()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id = await repo.InsertDiscoveredAsync("/media/bulk-never-play-idempotent.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "IdempotentBulkArtist"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "IdempotentBulkArtist");

            await ratingRepo.BulkSetNeverPlayAsync(filter, true, scope, CancellationToken.None);
            await ratingRepo.BulkSetNeverPlayAsync(filter, true, scope, CancellationToken.None);

            // One rating row, still flagged — the repeat sweep never duplicated or clobbered it.
            Assert.Equal(1, await CountRatingRowsAsync(db, id));
        }

        [Fact]
        public async Task NeverPlayFalseRestoresEveryMatchingRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/bulk-never-play-restore-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResultWith(genre: "BulkRestoreGenre"), CancellationToken.None);
            var id2 = await repo.InsertDiscoveredAsync("/media/bulk-never-play-restore-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResultWith(genre: "BulkRestoreGenre"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(GenresExact: ["BulkRestoreGenre"]);

            await ratingRepo.BulkSetNeverPlayAsync(filter, true, scope, CancellationToken.None);
            await ratingRepo.BulkSetNeverPlayAsync(filter, false, scope, CancellationToken.None);

            var ratings = await ratingRepo.GetRatingsAsync([id1.ToString(), id2.ToString()], CancellationToken.None);
            Assert.All(ratings, r => Assert.False(r.NeverPlay));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPreviewAndSweepAgree(DatabaseFixture db)
    {
        [Fact]
        public async Task AnExactArtistFilterAffectsExactlyTheBrowseResult()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var queenId = await repo.InsertDiscoveredAsync("/media/bulk-rating-queen.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(queenId, Harness.ReadyResultWith(artist: "Queen"), CancellationToken.None);
            var lookalikeId = await repo.InsertDiscoveredAsync("/media/bulk-rating-queensryche.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lookalikeId, Harness.ReadyResultWith(artist: "Queensrÿche"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "Queen");

            var browse = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);
            var affected = await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, scope, CancellationToken.None);

            Assert.Equal(browse.Total, affected);
        }

        [Fact]
        public async Task TheLookalikeArtistIsUntouched()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var queenId = await repo.InsertDiscoveredAsync("/media/bulk-rating-survive-queen.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(queenId, Harness.ReadyResultWith(artist: "Queen"), CancellationToken.None);
            var lookalikeId = await repo.InsertDiscoveredAsync("/media/bulk-rating-survive-queensryche.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lookalikeId, Harness.ReadyResultWith(artist: "Queensrÿche"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "Queen");

            await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, scope, CancellationToken.None);

            // Untouched means still the F33.2 ledger default — no rating row was ever created for it.
            var rating = Assert.Single(await ratingRepo.GetRatingsAsync([lookalikeId.ToString()], CancellationToken.None));
            Assert.Equal(50, rating.Score);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBulkRankingIsScopeBounded(DatabaseFixture db)
    {
        [Fact]
        public async Task TheDefaultSweepNeverTouchesOutOfScopeRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            var lib2Id = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.library (name) values ('BulkRatingScopeLib2') returning id");

            var inScopeId = await repo.InsertDiscoveredAsync("/media/bulk-rating-scope-in.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(inScopeId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var outScopeId = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.media (path, format, size_bytes, mtime, library_id) " +
                "values ('/media/bulk-rating-scope-out.flac', 'flac', 1, @mtime, @lib2Id) returning id",
                new { mtime = Harness.Mtime, lib2Id });
            await repo.WriteEnrichmentAsync(outScopeId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            // Default station scope: library 1 only. No filter beyond scope.
            var scope = new LibraryScope([1L]);
            await ratingRepo.BulkVoteAsync(new MediaQuery(), VoteDirection.Up, scope, CancellationToken.None);

            // The out-of-scope row never got a rating row at all — it reads the untouched default.
            var rating = Assert.Single(await ratingRepo.GetRatingsAsync([outScopeId.ToString()], CancellationToken.None));
            Assert.Equal(50, rating.Score);
        }

        [Fact]
        public async Task ANamedLibraryIdBecomesTheEffectiveScope()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            var lib2Id = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.library (name) values ('BulkRatingNamedLib2') returning id");

            var lib1Id = await repo.InsertDiscoveredAsync("/media/bulk-rating-named-lib1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lib1Id, Harness.ReadyResultWith(artist: "NamedScopeArtist"), CancellationToken.None);

            var lib2MediaId = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.media (path, format, size_bytes, mtime, library_id) " +
                "values ('/media/bulk-rating-named-lib2.flac', 'flac', 1, @mtime, @lib2Id) returning id",
                new { mtime = Harness.Mtime, lib2Id });
            await repo.WriteEnrichmentAsync(lib2MediaId, Harness.ReadyResultWith(artist: "NamedScopeArtist"), CancellationToken.None);

            // Effective scope narrowed to library 2 only — the F23.3 named-library swap, already
            // resolved to a LibraryScope by the controller before reaching this seam (mirrors
            // Story148's ANamedLibraryScopeReturnsOnlyThatLibrarysValues).
            var namedScope = new LibraryScope([lib2Id]);
            var filter = new MediaQuery(ArtistExact: "NamedScopeArtist", LibraryId: lib2Id);

            await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, namedScope, CancellationToken.None);

            var lib1Rating = Assert.Single(await ratingRepo.GetRatingsAsync([lib1Id.ToString()], CancellationToken.None));
            Assert.Equal(50, lib1Rating.Score);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRatingsNeverDisturbCatalogRows(DatabaseFixture db)
    {
        [Fact]
        public async Task ABulkVoteLeavesMediaXminUnchanged()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id = await repo.InsertDiscoveredAsync("/media/bulk-rating-xmin-vote.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "XminVoteBulkArtist"), CancellationToken.None);

            var xminBefore = await ReadMediaXminAsync(db, id);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "XminVoteBulkArtist");
            await ratingRepo.BulkVoteAsync(filter, VoteDirection.Up, scope, CancellationToken.None);

            var xminAfter = await ReadMediaXminAsync(db, id);
            Assert.Equal(xminBefore, xminAfter);
        }

        [Fact]
        public async Task ABulkNeverPlayLeavesMediaXminUnchanged()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var ratingRepo = RatingRepo(db);

            var id = await repo.InsertDiscoveredAsync("/media/bulk-rating-xmin-never-play.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "XminNeverPlayBulkArtist"), CancellationToken.None);

            var xminBefore = await ReadMediaXminAsync(db, id);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "XminNeverPlayBulkArtist");
            await ratingRepo.BulkSetNeverPlayAsync(filter, true, scope, CancellationToken.None);

            var xminAfter = await ReadMediaXminAsync(db, id);
            Assert.Equal(xminBefore, xminAfter);
        }
    }
}
