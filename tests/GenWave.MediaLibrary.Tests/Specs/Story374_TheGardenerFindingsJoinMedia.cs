// STORY-374 — The gardener's findings queue, joined to the media it is about (SPEC F153.9 · PLAN T377)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture. Two arrangements:
// RotFindingRepository.ListWithMediaAsync's own plays/rating join (a media_rotation ledger row PLUS
// a media_rating row, vs. neither — proving the 0/null defaults T377's own admin listing depends on),
// and the shared ClampPaging floor the T372 review LOW-2 finding demands (limit 0 still returns one
// row; a negative offset — which errors in Postgres's own raw OFFSET clause — never throws here).
// GardenerController's own endpoint clamp (Host.Tests, Story374_TheGardenerTendsAQueue.cs) is a
// courtesy on top of this; THIS file is what proves the bound is callee-enforced regardless.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Garden;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTheGardenerFindingsJoinMedia
{
    static RotFindingRepository Repo(DatabaseFixture db) => new(db.DataSource);

    /// <summary>A ready row carrying the tag/duration values <c>ListWithMediaAsync</c>'s own
    /// <c>media</c> projection surfaces — no <c>measurable</c>/playable-predicate concern here, this
    /// query joins plainly rather than filtering on it.</summary>
    static async Task<long> InsertReadyRowAsync(DatabaseFixture db, string path, string artist, string title, int durationMs)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, duration_ms, artist, title, eligible)
            values (@path, 'flac', 1024, now(), 'ready', @durationMs, @artist, @title, true)
            returning id
            """,
            new { path, durationMs, artist, title });
    }

    static async Task InsertRotationLedgerAsync(DatabaseFixture db, long mediaId, int playCount)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rotation (media_id, play_count) values (@mediaId, @playCount)",
            new { mediaId, playCount });
    }

    static async Task InsertRatingAsync(DatabaseFixture db, long mediaId, int score)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rating (media_id, score) values (@mediaId, @score)",
            new { mediaId, score });
    }

    static async Task InsertFindingAsync(DatabaseFixture db, long mediaId, string kindText)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into library.rot_finding (media_id, kind, state, evidence)
            values (@mediaId, @kind::library.rot_kind, 'open', '{}')
            """,
            new { mediaId, kind = kindText });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePlaysAndRatingJoin(DatabaseFixture db)
    {
        // Given a row carrying a media_rotation ledger row (play_count 3) and a media_rating row
        // (score 80), When ListWithMediaAsync reads it back.
        [Fact]
        public async Task ARowWithALedgerAndRatingCarriesTheLedgerPlayCount()
        {
            await db.ResetAsync();
            var mediaId = await InsertReadyRowAsync(db, "/gardener/t377-join-a.flac", "Artist", "Song A", 200_000);
            await InsertRotationLedgerAsync(db, mediaId, playCount: 3);
            await InsertRatingAsync(db, mediaId, score: 80);
            await InsertFindingAsync(db, mediaId, "dead_file");

            var rows = (await Repo(db).ListWithMediaAsync(RotKind.DeadFile, RotState.Open, 200, 0, CancellationToken.None)).Items;

            Assert.Equal(3, Assert.Single(rows).Plays);
        }

        [Fact]
        public async Task ARowWithALedgerAndRatingCarriesTheRatingScore()
        {
            await db.ResetAsync();
            var mediaId = await InsertReadyRowAsync(db, "/gardener/t377-join-a2.flac", "Artist", "Song A2", 200_000);
            await InsertRotationLedgerAsync(db, mediaId, playCount: 3);
            await InsertRatingAsync(db, mediaId, score: 80);
            await InsertFindingAsync(db, mediaId, "dead_file");

            var rows = (await Repo(db).ListWithMediaAsync(RotKind.DeadFile, RotState.Open, 200, 0, CancellationToken.None)).Items;

            Assert.Equal(80, Assert.Single(rows).Rating);
        }

        // Given a row with NO media_rotation row and NO media_rating row, When ListWithMediaAsync
        // reads it back — plays defaults to 0 (never absent), rating stays null (never the F33.2
        // ledger default of 50).
        [Fact]
        public async Task ARowWithNeitherDefaultsPlaysToZero()
        {
            await db.ResetAsync();
            var mediaId = await InsertReadyRowAsync(db, "/gardener/t377-join-b.flac", "Artist", "Song B", 200_000);
            await InsertFindingAsync(db, mediaId, "dead_file");

            var rows = (await Repo(db).ListWithMediaAsync(RotKind.DeadFile, RotState.Open, 200, 0, CancellationToken.None)).Items;

            Assert.Equal(0, Assert.Single(rows).Plays);
        }

        [Fact]
        public async Task ARowWithNeitherLeavesRatingNull()
        {
            await db.ResetAsync();
            var mediaId = await InsertReadyRowAsync(db, "/gardener/t377-join-b2.flac", "Artist", "Song B2", 200_000);
            await InsertFindingAsync(db, mediaId, "dead_file");

            var rows = (await Repo(db).ListWithMediaAsync(RotKind.DeadFile, RotState.Open, 200, 0, CancellationToken.None)).Items;

            Assert.Null(Assert.Single(rows).Rating);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePagingFloorIsCalleeEnforced(DatabaseFixture db)
    {
        // Given two open findings, When ListWithMediaAsync is called with limit 0 — the repository's
        // own ClampPaging floors it to 1 regardless of what the caller passed.
        [Fact]
        public async Task LimitZeroReturnsExactlyOneRow()
        {
            await db.ResetAsync();
            var firstId = await InsertReadyRowAsync(db, "/gardener/t377-floor-a.flac", "Artist", "Song A", 200_000);
            var secondId = await InsertReadyRowAsync(db, "/gardener/t377-floor-b.flac", "Artist", "Song B", 200_000);
            await InsertFindingAsync(db, firstId, "dead_file");
            await InsertFindingAsync(db, secondId, "stale_metadata");

            var rows = (await Repo(db).ListWithMediaAsync(null, RotState.Open, limit: 0, offset: 0, ct: CancellationToken.None)).Items;

            Assert.Single(rows);
        }

        // Given one open finding, When ListWithMediaAsync is called with a negative offset — a raw
        // negative OFFSET errors in Postgres, so a non-throwing result here is direct proof the
        // repository's own floor (never the caller) is what stands between a bad value and that error.
        [Fact]
        public async Task NegativeOffsetDoesNotThrow()
        {
            await db.ResetAsync();
            var mediaId = await InsertReadyRowAsync(db, "/gardener/t377-floor-c.flac", "Artist", "Song C", 200_000);
            await InsertFindingAsync(db, mediaId, "dead_file");

            var rows = (await Repo(db).ListWithMediaAsync(null, RotState.Open, limit: 200, offset: -1, ct: CancellationToken.None)).Items;

            Assert.Single(rows);
        }
    }
}
