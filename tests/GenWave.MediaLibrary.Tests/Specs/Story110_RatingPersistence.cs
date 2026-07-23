// STORY-110 — Rating persistence: clamped votes, idempotent flag, batch read
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection — the
// ON CONFLICT clamp math and the votes-don't-bump-xmin proof are exactly the fake-vs-wire
// class R8/Q2 taught (MEMORY.md 2026-07-11); a fake IMediaRating cannot certify either.
// S2 lands MediaRatingRepository; see docs/PLAN.md Epic S.

using GenWave.MediaLibrary.Tests.Fakes;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRatingPersistence
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static MediaRatingRepository RatingRepo(DatabaseFixture db) => new(db.DataSource, new FakeSafeScopeProvider());

    /// <summary>Inserts a fresh library.media row (state='discovered') and returns its id.</summary>
    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO library.media (path, format, size_bytes, mtime, state, library_id)
              VALUES (@path, 'flac', 1024, now(), 'discovered', 1)
              RETURNING id",
            new { path });
    }

    static async Task<string> ReadXminAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string>(
            "select xmin::text from library.media where id = @id", new { id }) ?? "";
    }

    static async Task<int> CountRatingRowsAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from library.media_rating where media_id = @id", new { id });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — lazy upsert + clamping
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFirstVoteOnAnUnratedRow(DatabaseFixture db)
    {
        [Fact]
        public async Task AVoteUpCreatesTheRowAtFiftyOne()
        {
            // Arrange: a seeded media row with NO media_rating row.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/rating/first-vote-up.flac");
            var repo = RatingRepo(db);

            // Act: VoteAsync(up). Assert: exactly one rating row exists with score 51 (F33.2–F33.3).
            var outcome = await repo.VoteAsync(id.ToString(), VoteDirection.Up, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, outcome.Result);
            Assert.Equal(51, outcome.Score);
            Assert.Equal(1, await CountRatingRowsAsync(db, id));
        }

        [Fact]
        public async Task AVoteDownCreatesTheRowAtFortyNine()
        {
            // Same arrange; VoteAsync(down) → score 49.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/rating/first-vote-down.flac");
            var repo = RatingRepo(db);

            var outcome = await repo.VoteAsync(id.ToString(), VoteDirection.Down, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, outcome.Result);
            Assert.Equal(49, outcome.Score);
            Assert.Equal(1, await CountRatingRowsAsync(db, id));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVotesClampAtTheBounds(DatabaseFixture db)
    {
        [Fact]
        public async Task AVoteUpAtOneHundredStaysAtOneHundred()
        {
            // Arrange: rating row at score 100. Act: VoteAsync(up) succeeds. Assert: score is 100 (F33.3).
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/rating/clamp-top.flac");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "insert into library.media_rating (media_id, score) values (@id, 100)", new { id });

            var repo = RatingRepo(db);
            var outcome = await repo.VoteAsync(id.ToString(), VoteDirection.Up, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, outcome.Result);
            Assert.Equal(100, outcome.Score);
        }

        [Fact]
        public async Task AVoteDownAtZeroStaysAtZero()
        {
            // Arrange: rating row at score 0. Act: VoteAsync(down) succeeds. Assert: score is 0 (F33.3).
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/rating/clamp-bottom.flac");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "insert into library.media_rating (media_id, score) values (@id, 0)", new { id });

            var repo = RatingRepo(db);
            var outcome = await repo.VoteAsync(id.ToString(), VoteDirection.Down, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, outcome.Result);
            Assert.Equal(0, outcome.Score);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverPlaySetIsIdempotent(DatabaseFixture db)
    {
        [Fact]
        public async Task SettingTrueTwiceLeavesOneRowFlaggedTrue()
        {
            // SetNeverPlayAsync(true) twice → one row, never_play = true (F33.4).
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/rating/never-play-idempotent.flac");
            var repo = RatingRepo(db);

            var first = await repo.SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);
            var second = await repo.SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, first.Result);
            Assert.True(first.NeverPlay);
            Assert.Equal(RatingWriteResult.Updated, second.Result);
            Assert.True(second.NeverPlay);
            Assert.Equal(1, await CountRatingRowsAsync(db, id));
        }

        [Fact]
        public async Task SettingFalseAfterTrueReadsFalse()
        {
            // The third set (false) round-trips; still exactly one row.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/rating/never-play-round-trip.flac");
            var repo = RatingRepo(db);

            await repo.SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);
            await repo.SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);
            var third = await repo.SetNeverPlayAsync(id.ToString(), false, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, third.Result);
            Assert.False(third.NeverPlay);
            Assert.Equal(1, await CountRatingRowsAsync(db, id));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBatchReadResolvesDefaults(DatabaseFixture db)
    {
        [Fact]
        public async Task ARatedAndAnUnratedIdBothReturn()
        {
            // GetRatingsAsync over one rated + one unrated id returns both entries (F33.9).
            await db.ResetAsync();
            var ratedId = await InsertMediaRowAsync(db, "/rating/batch-rated.flac");
            var unratedId = await InsertMediaRowAsync(db, "/rating/batch-unrated.flac");
            var repo = RatingRepo(db);

            await repo.VoteAsync(ratedId.ToString(), VoteDirection.Up, CancellationToken.None);

            var results = await repo.GetRatingsAsync(
                [ratedId.ToString(), unratedId.ToString()], CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.MediaId == ratedId.ToString());
            Assert.Contains(results, r => r.MediaId == unratedId.ToString());
        }

        [Fact]
        public async Task TheUnratedEntryCarriesScoreFiftyAndPlayable()
        {
            // The unrated id reads score 50 / never_play false — absent row MEANS defaults (F33.2).
            await db.ResetAsync();
            var unratedId = await InsertMediaRowAsync(db, "/rating/batch-defaults.flac");
            var repo = RatingRepo(db);

            var results = await repo.GetRatingsAsync([unratedId.ToString()], CancellationToken.None);

            var entry = Assert.Single(results);
            Assert.Equal(unratedId.ToString(), entry.MediaId);
            Assert.Equal(50, entry.Score);
            Assert.False(entry.NeverPlay);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVotesNeverTouchTheMediaRowVersion(DatabaseFixture db)
    {
        [Fact]
        public async Task TheMediaRowXminIsUnchangedAfterVotesAndFlagSets()
        {
            // Capture library.media.xmin::text before; apply votes + never-play sets;
            // xmin unchanged — an F18.6 PATCH with the pre-vote version still succeeds (F33.1).
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/rating/xmin-stability.flac");
            var repo = RatingRepo(db);

            var xminBefore = await ReadXminAsync(db, id);

            await repo.VoteAsync(id.ToString(), VoteDirection.Up, CancellationToken.None);
            await repo.VoteAsync(id.ToString(), VoteDirection.Up, CancellationToken.None);
            await repo.VoteAsync(id.ToString(), VoteDirection.Down, CancellationToken.None);
            await repo.SetNeverPlayAsync(id.ToString(), true, CancellationToken.None);
            await repo.SetNeverPlayAsync(id.ToString(), false, CancellationToken.None);

            var xminAfter = await ReadXminAsync(db, id);

            Assert.Equal(xminBefore, xminAfter);

            // The AC's actual point: a PATCH bearing the pre-vote ETag still succeeds after votes land.
            var mediaRepo = Harness.Repo(db);
            var patch = new MediaPatch(Title: "Post-Vote Title", Artist: null, Album: null, Genre: null, Year: null, Eligible: null, LibraryId: null);
            var scope = new LibraryScope([1L]);

            var result = await ((IAdminMediaWrite)mediaRepo).UpdateAsync(
                id.ToString(), patch, xminBefore, scope, CancellationToken.None);

            Assert.Equal(MediaWriteResult.Updated, result);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — FK integrity
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVoteOnANonexistentMediaId(DatabaseFixture db)
    {
        [Fact]
        public async Task TheSeamReportsNotFoundAndWritesNothing()
        {
            // VoteAsync on an id with no library.media row → not-found result; no rating row created.
            await db.ResetAsync();
            var repo = RatingRepo(db);
            const long ghostId = 999_999L;

            var outcome = await repo.VoteAsync(ghostId.ToString(), VoteDirection.Up, CancellationToken.None);

            Assert.Equal(RatingWriteResult.NotFound, outcome.Result);
            Assert.Null(outcome.Score);
            Assert.Equal(0, await CountRatingRowsAsync(db, ghostId));
        }
    }
}
