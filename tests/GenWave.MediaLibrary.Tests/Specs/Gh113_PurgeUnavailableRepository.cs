// gh-#113 — Explicit operator purge for long-unavailable tracks (commit 2: the repository half).
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection.
// PurgeUnavailableAsync's whole decision is ONE SQL statement — candidate set (unavailable longer
// than the window, non-NULL unavailable_since), library total, tripwire comparison, and the
// conditionally-withheld DELETE — so these facts exercise the real thing: the age filter, the
// media_rating ON DELETE CASCADE, the >50% mount-outage tripwire (with its exactly-half boundary),
// dry runs, and the never-purge-a-NULL-stamp fail-safe.

using Dapper;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePurgeUnavailableRepository
{
    // ---------------------------------------------------------------------
    // Helpers (spec-local, the Story242 convention)
    // ---------------------------------------------------------------------

    static async Task<long> InsertRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime)
            values (@path, 'flac', 100, now())
            returning id
            """, new { path });
    }

    static async Task<long> InsertUnavailableRowAsync(DatabaseFixture db, string path, string sinceInterval)
    {
        var id = await InsertRowAsync(db, path);
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set state = 'unavailable', unavailable_since = now() - @since::interval where id = @id",
            new { id, since = sinceInterval });
        return id;
    }

    static async Task InsertRatingAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rating (media_id, score, never_play) values (@mediaId, 80, false)",
            new { mediaId });
    }

    static async Task<bool> RowExistsAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from library.media where id = @id)", new { id });
    }

    static async Task<int> RatingCountAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from library.media_rating where media_id = @mediaId", new { mediaId });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the age filter
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAgeFilter(DatabaseFixture db)
    {
        [Fact]
        public async Task Only_rows_unavailable_longer_than_the_window_are_deleted()
        {
            await db.ResetAsync();
            var old = await InsertUnavailableRowAsync(db, "/media/old.flac", "10 days");
            var recent = await InsertUnavailableRowAsync(db, "/media/recent.flac", "2 days");
            var here = await InsertRowAsync(db, "/media/here.flac");
            // A fourth row keeps the candidate share at exactly half (1 of 4 candidates would be
            // 25%; without it 1 of 3 is 33% — either passes, but the extra row keeps this fact
            // about the AGE filter, not the tripwire).
            await InsertRowAsync(db, "/media/also-here.flac");
            var repo = Harness.Repo(db);

            var outcome = await repo.PurgeUnavailableAsync(olderThanDays: 7, dryRun: false, CancellationToken.None);

            Assert.Equal(new MediaPurgeOutcome(Candidates: 1, LibraryTotal: 4, Deleted: 1), outcome);
            Assert.False(await RowExistsAsync(db, old));
            Assert.True(await RowExistsAsync(db, recent));
            Assert.True(await RowExistsAsync(db, here));
        }

        [Fact]
        public async Task An_unavailable_row_with_no_stamp_is_never_purgeable()
        {
            // The fail-safe: how long a NULL-stamped row has been gone is unknowable (it predates
            // db/28, or something bypassed MarkUnavailableAsync) — deleting on a guess is worse
            // than keeping a dead row until a scan or the migration stamps it.
            await db.ResetAsync();
            var unstamped = await InsertUnavailableRowAsync(db, "/media/unstamped.flac", "10 days");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync(
                    "update library.media set unavailable_since = null where id = @id", new { id = unstamped });
            }
            await InsertRowAsync(db, "/media/here.flac");
            var repo = Harness.Repo(db);

            var outcome = await repo.PurgeUnavailableAsync(olderThanDays: 7, dryRun: false, CancellationToken.None);

            Assert.Equal(0, outcome.Candidates);
            Assert.True(await RowExistsAsync(db, unstamped));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — dependent rows cascade
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDependentRowsCascade(DatabaseFixture db)
    {
        [Fact]
        public async Task A_purged_rows_rating_goes_with_it_and_a_survivors_rating_stays()
        {
            await db.ResetAsync();
            var old = await InsertUnavailableRowAsync(db, "/media/old.flac", "10 days");
            var here = await InsertRowAsync(db, "/media/here.flac");
            await InsertRowAsync(db, "/media/also-here.flac");
            await InsertRatingAsync(db, old);
            await InsertRatingAsync(db, here);
            var repo = Harness.Repo(db);

            var outcome = await repo.PurgeUnavailableAsync(olderThanDays: 7, dryRun: false, CancellationToken.None);

            Assert.Equal(1, outcome.Deleted);
            Assert.Equal(0, await RatingCountAsync(db, old));
            Assert.Equal(1, await RatingCountAsync(db, here));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the mount-outage tripwire
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMountOutageTripwire(DatabaseFixture db)
    {
        [Fact]
        public async Task Candidates_over_half_the_library_delete_nothing()
        {
            // 2 of 3 — the shrunk-mount pattern gh-#113's design singled out: most of the catalog
            // flips unavailable together, and the purge must refuse to compound the outage.
            await db.ResetAsync();
            var goneA = await InsertUnavailableRowAsync(db, "/media/gone-a.flac", "10 days");
            var goneB = await InsertUnavailableRowAsync(db, "/media/gone-b.flac", "10 days");
            await InsertRowAsync(db, "/media/here.flac");
            var repo = Harness.Repo(db);

            var outcome = await repo.PurgeUnavailableAsync(olderThanDays: 7, dryRun: false, CancellationToken.None);

            Assert.True(outcome.TripwireTripped);
            Assert.Equal(new MediaPurgeOutcome(Candidates: 2, LibraryTotal: 3, Deleted: 0), outcome);
            Assert.True(await RowExistsAsync(db, goneA));
            Assert.True(await RowExistsAsync(db, goneB));
        }

        [Fact]
        public async Task Exactly_half_the_library_is_allowed_through()
        {
            // The boundary is contract: "exceed 50%" refuses, exactly 50% purges.
            await db.ResetAsync();
            var goneA = await InsertUnavailableRowAsync(db, "/media/gone-a.flac", "10 days");
            var goneB = await InsertUnavailableRowAsync(db, "/media/gone-b.flac", "10 days");
            await InsertRowAsync(db, "/media/here.flac");
            await InsertRowAsync(db, "/media/also-here.flac");
            var repo = Harness.Repo(db);

            var outcome = await repo.PurgeUnavailableAsync(olderThanDays: 7, dryRun: false, CancellationToken.None);

            Assert.False(outcome.TripwireTripped);
            Assert.Equal(2, outcome.Deleted);
            Assert.False(await RowExistsAsync(db, goneA));
            Assert.False(await RowExistsAsync(db, goneB));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — dry run
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDryRun(DatabaseFixture db)
    {
        [Fact]
        public async Task A_dry_run_counts_the_candidates_but_deletes_nothing()
        {
            await db.ResetAsync();
            var old = await InsertUnavailableRowAsync(db, "/media/old.flac", "10 days");
            await InsertRowAsync(db, "/media/here.flac");
            await InsertRowAsync(db, "/media/also-here.flac");
            var repo = Harness.Repo(db);

            var outcome = await repo.PurgeUnavailableAsync(olderThanDays: 7, dryRun: true, CancellationToken.None);

            Assert.Equal(new MediaPurgeOutcome(Candidates: 1, LibraryTotal: 3, Deleted: 0), outcome);
            Assert.True(await RowExistsAsync(db, old));
        }

        [Fact]
        public async Task A_dry_run_reports_the_tripwire_the_real_purge_would_hit()
        {
            await db.ResetAsync();
            await InsertUnavailableRowAsync(db, "/media/gone-a.flac", "10 days");
            await InsertUnavailableRowAsync(db, "/media/gone-b.flac", "10 days");
            await InsertRowAsync(db, "/media/here.flac");
            var repo = Harness.Repo(db);

            var outcome = await repo.PurgeUnavailableAsync(olderThanDays: 7, dryRun: true, CancellationToken.None);

            Assert.True(outcome.TripwireTripped);
            Assert.Equal(0, outcome.Deleted);
        }
    }
}
