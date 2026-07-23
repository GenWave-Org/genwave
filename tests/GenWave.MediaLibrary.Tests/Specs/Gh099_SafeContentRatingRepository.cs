// gh-#99 — Safe loop / station IDs must never allow ranking or taste voting: the REPOSITORY half.
//
// BDD specification — xUnit, Postgres-backed (Category=Integration, shared DatabaseFixture): the
// safe-scope carve-outs are SQL-side behavior a fake store would never exercise honestly —
//   • VoteAsync/SetNeverPlayAsync answer SafeContentExcluded for a row whose library_id falls in
//     the live safe scope, and write NOTHING;
//   • GetRatingsAsync stamps such rows rateable: false (music rows stay true);
//   • the bulk sweeps' WHERE carve-out matches zero safe rows even when the effective scope IS the
//     safe library (the named-library override path);
//   • ListAdminAsync projects the same rateable flag for the catalog UI;
//   • booth_log.media_id (db/22) round-trips through AppendAsync → ReadAsync/GetMediaIdAsync so the
//     Host can resolve safe membership for the taste-thumb refusal.
//
// Controller-level facts live in GenWave.Host.Tests/Specs/Gh099_SafeContentTasteExclusion.cs.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Threading.Channels;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureSafeContentRatingRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Creates a fresh safe library plus one media row in it and one in the default library (id 1).</summary>
    static async Task<(long SafeLibraryId, long SafeMediaId, long MusicMediaId)> SeedAsync(DatabaseFixture db, string tag)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var safeLibraryId = await conn.ExecuteScalarAsync<long>(
            "insert into library.library (name) values (@name) returning id", new { name = $"safe-{tag}" });
        var safeMediaId = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO library.media (path, format, size_bytes, mtime, state, library_id)
              VALUES (@path, 'flac', 1024, now(), 'ready', @lib) RETURNING id",
            new { path = $"/media/{tag}-safe.flac", lib = safeLibraryId });
        var musicMediaId = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO library.media (path, format, size_bytes, mtime, state, library_id)
              VALUES (@path, 'flac', 1024, now(), 'ready', 1) RETURNING id",
            new { path = $"/media/{tag}-music.flac" });
        return (safeLibraryId, safeMediaId, musicMediaId);
    }

    static MediaRatingRepository RatingRepo(DatabaseFixture db, long safeLibraryId) =>
        new(db.DataSource, new FakeSafeScopeProvider(safeLibraryId));

    static async Task<int> CountRatingRowsAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from library.media_rating where media_id = @id", new { id });
    }

    // ---------------------------------------------------------------------
    // Per-row writes refuse safe content
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPerRowWritesRefuseSafeContent(DatabaseFixture db)
    {
        [Fact]
        public async Task AVoteOnASafeRowAnswersExcludedAndWritesNothing()
        {
            await db.ResetAsync();
            var (safeLib, safeMedia, _) = await SeedAsync(db, "vote");
            var repo = RatingRepo(db, safeLib);

            var outcome = await repo.VoteAsync(safeMedia.ToString(), VoteDirection.Up, CancellationToken.None);

            Assert.Equal(RatingWriteResult.SafeContentExcluded, outcome.Result);
            Assert.Equal(0, await CountRatingRowsAsync(db, safeMedia));
        }

        [Fact]
        public async Task ANeverPlaySetOnASafeRowAnswersExcludedAndWritesNothing()
        {
            await db.ResetAsync();
            var (safeLib, safeMedia, _) = await SeedAsync(db, "np");
            var repo = RatingRepo(db, safeLib);

            var outcome = await repo.SetNeverPlayAsync(safeMedia.ToString(), true, CancellationToken.None);

            Assert.Equal(RatingWriteResult.SafeContentExcluded, outcome.Result);
            Assert.Equal(0, await CountRatingRowsAsync(db, safeMedia));
        }

        [Fact]
        public async Task AVoteOnAMusicRowStillWrites()
        {
            await db.ResetAsync();
            var (safeLib, _, musicMedia) = await SeedAsync(db, "music-vote");
            var repo = RatingRepo(db, safeLib);

            var outcome = await repo.VoteAsync(musicMedia.ToString(), VoteDirection.Up, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, outcome.Result);
            Assert.Equal(51, outcome.Score);
        }

        [Fact]
        public async Task AnEmptySafeScopeExcludesNothing()
        {
            // The pre-#99 behavior: no safe scope configured, everything rateable.
            await db.ResetAsync();
            var (_, safeMedia, _) = await SeedAsync(db, "empty-scope");
            var repo = new MediaRatingRepository(db.DataSource, new FakeSafeScopeProvider());

            var outcome = await repo.VoteAsync(safeMedia.ToString(), VoteDirection.Up, CancellationToken.None);

            Assert.Equal(RatingWriteResult.Updated, outcome.Result);
        }
    }

    // ---------------------------------------------------------------------
    // Batch read stamps rateable
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBatchReadStampsRateable(DatabaseFixture db)
    {
        [Fact]
        public async Task SafeRowsReadBackRateableFalseAndMusicRowsTrue()
        {
            await db.ResetAsync();
            var (safeLib, safeMedia, musicMedia) = await SeedAsync(db, "batch");
            var repo = RatingRepo(db, safeLib);

            var ratings = await repo.GetRatingsAsync(
                [safeMedia.ToString(), musicMedia.ToString()], CancellationToken.None);

            Assert.False(ratings.Single(r => r.MediaId == safeMedia.ToString()).Rateable);
            Assert.True(ratings.Single(r => r.MediaId == musicMedia.ToString()).Rateable);
        }
    }

    // ---------------------------------------------------------------------
    // Bulk sweeps never match safe rows
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBulkSweepsCarveOutSafeRows(DatabaseFixture db)
    {
        [Fact]
        public async Task ABulkVoteScopedToTheSafeLibraryItselfUpdatesNothing()
        {
            // The named-library override path (F23.3): even when the EFFECTIVE scope is exactly the
            // safe library, the WHERE carve-out matches zero rows — belt to the controller's braces.
            await db.ResetAsync();
            var (safeLib, safeMedia, _) = await SeedAsync(db, "bulk");
            var repo = RatingRepo(db, safeLib);

            var updated = await repo.BulkVoteAsync(
                new MediaQuery(), VoteDirection.Up, new LibraryScope([safeLib]), CancellationToken.None);

            Assert.Equal(0, updated);
            Assert.Equal(0, await CountRatingRowsAsync(db, safeMedia));
        }

        [Fact]
        public async Task ABulkNeverPlayOverAMixedScopeOnlyTouchesMusicRows()
        {
            await db.ResetAsync();
            var (safeLib, safeMedia, musicMedia) = await SeedAsync(db, "bulk-mixed");
            var repo = RatingRepo(db, safeLib);

            var updated = await repo.BulkSetNeverPlayAsync(
                new MediaQuery(), true, new LibraryScope([1, safeLib]), CancellationToken.None);

            Assert.True(updated >= 1);
            Assert.Equal(0, await CountRatingRowsAsync(db, safeMedia));
            Assert.Equal(1, await CountRatingRowsAsync(db, musicMedia));
        }
    }

    // ---------------------------------------------------------------------
    // Admin list projects rateable for the catalog UI
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAdminListProjectsRateable(DatabaseFixture db)
    {
        [Fact]
        public async Task SafeRowsListRateableFalseAndMusicRowsTrue()
        {
            await db.ResetAsync();
            var (safeLib, safeMedia, musicMedia) = await SeedAsync(db, "list");
            var repo = new MediaRepository(
                db.DataSource, NullLogger<MediaRepository>.Instance, Channel.CreateUnbounded<long>(),
                new FakeSafeScopeProvider(safeLib));

            var page = await repo.ListAdminAsync(
                new LibraryScope([1, safeLib]), new MediaQuery(), CancellationToken.None);

            Assert.False(page.Items.Single(i => i.MediaId == safeMedia.ToString()).Rateable);
            Assert.True(page.Items.Single(i => i.MediaId == musicMedia.ToString()).Rateable);
        }
    }

    // ---------------------------------------------------------------------
    // booth_log.media_id round trip (db/22)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBoothLogMediaIdRoundTrip(DatabaseFixture db)
    {
        static BoothLogRepository Repo(DatabaseFixture f) => new(
            new Lazy<NpgsqlDataSource>(() => f.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new BoothLogOptions()));

        [Fact]
        public async Task AnAppendedMediaIdReadsBackThroughBothReadSeams()
        {
            await db.ResetBoothLogAsync();
            var repo = Repo(db);

            await repo.AppendAsync(
                "track-started", "Started 'Song' by Someone", personaId: null, artist: "Someone",
                pick: null, mediaId: 42, CancellationToken.None);

            var page = await repo.ReadAsync(before: null, take: 1, CancellationToken.None);
            var entry = Assert.Single(page.Entries);
            Assert.Equal(42, entry.MediaId);
            Assert.Equal(42, await repo.GetMediaIdAsync(entry.Id, CancellationToken.None));
        }

        [Fact]
        public async Task AnUnstampedRowAnswersNullFromBothReadSeams()
        {
            await db.ResetBoothLogAsync();
            var repo = Repo(db);

            await repo.AppendAsync(
                "patter-aired", "Patter aired (station-id)", personaId: null, artist: null,
                pick: null, mediaId: null, CancellationToken.None);

            var page = await repo.ReadAsync(before: null, take: 1, CancellationToken.None);
            var entry = Assert.Single(page.Entries);
            Assert.Null(entry.MediaId);
            Assert.Null(await repo.GetMediaIdAsync(entry.Id, CancellationToken.None));
        }
    }
}
