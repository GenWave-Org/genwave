// STORY-373 — I can install and tune Deep Cuts: real-Postgres proof for the two new SQL reads
// (SPEC F152.5 · PLAN T362, T362 review HIGH-2)
//
// BDD specification — xUnit. Integration (Category=Integration, shared DatabaseFixture) — both
// reads under test (BoothLogRepository.GetLastAiringAsync's window-function CTE,
// MediaRepository.GetEnvelopeCandidateCountAsync's by-construction WHERE) are selection SQL,
// provable only against the real planner (Story212_EnvelopeCandidateQuery.cs's own rationale,
// mirrored). Neither had a single test before this file — every Host.Tests fact for T362 drove a
// fake/scripted double instead (the legitimate wire-layer scope Story305_ShowsApi.cs's own header
// documents), so the real query text itself — including the HIGH-1 nested-window-function bug that
// 500s every real station — went unexercised.
//
// GetLastAiringAsync's own helpers mirror Story195_BoothLogStore.cs's Store()/raw-SQL-insert idiom
// (station.booth_log.show_id carries no FK — "history must outlive the entity" — so seeding a bare
// long id needs no station.show row to exist first). GetEnvelopeCandidateCountAsync's own helpers
// mirror Story372_ThePoolHonoursTheRotationPredicate.cs's InsertReadyAsync/SeedRotationAsync idiom
// and StoryF3_BulkEligibilityByFilter.cs's own second-library-row idiom for the safe-scope-exclusion
// case (redefined here, not shared — this test project's own "duplicated rather than imported"
// convention, e.g. every one of those files' own header comments make the same call).

using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureLastAiringAndPoolCount
{
    // ---------------------------------------------------------------------
    // Helpers — GetLastAiringAsync
    // ---------------------------------------------------------------------

    static IBoothLogReader Reader(DatabaseFixture db, int retentionDays = 14) =>
        new BoothLogRepository(
            new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new BoothLogOptions { RetentionDays = retentionDays }));

    /// <summary>Inserts one "track-started" row directly (station.booth_log.show_id carries no FK,
    /// SPEC F121.1's own "history must outlive the entity" — a bare long id needs no station.show
    /// row behind it). <paramref name="rotationRelax"/> null omits the pick column entirely (a
    /// picked-with-no-relax-stamp row, or an engine-initiated play) — mirrors
    /// BoothLogPickStamp.RotationRelax's own "null means no RotationPredicate was in force at all"
    /// contract one layer up.</summary>
    static async Task InsertTrackStartedAsync(
        DatabaseFixture db, long showId, DateTimeOffset occurredAt, int? rotationRelax)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into station.booth_log (occurred_at, kind, summary, show_id, pick)
            values (@occurredAt, 'track-started', 'Started a track', @showId, @pick::jsonb)
            """,
            new
            {
                occurredAt,
                showId,
                pick = rotationRelax is null ? null : $"{{\"rotationRelax\": {rotationRelax}}}",
            });
    }

    // ---------------------------------------------------------------------
    // (a) STORY-373 AC3's own stamp pattern: RotationRelax 0,0,1,2 for one show
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheAc3StampPatternCounts(DatabaseFixture db)
    {
        // Given four track-started rows for one show, RotationRelax 0,0,1,2 (STORY-373 AC3's own
        // wording), When GetLastAiringAsync reads it back.
        [Fact]
        public async Task FourPicksTwoRelaxed()
        {
            await db.ResetBoothLogAsync();
            const long showId = 101;
            var t0 = DateTimeOffset.UtcNow.AddHours(-1);

            await InsertTrackStartedAsync(db, showId, t0, rotationRelax: 0);
            await InsertTrackStartedAsync(db, showId, t0.AddMinutes(3), rotationRelax: 0);
            await InsertTrackStartedAsync(db, showId, t0.AddMinutes(6), rotationRelax: 1);
            await InsertTrackStartedAsync(db, showId, t0.AddMinutes(9), rotationRelax: 2);

            var lastAiring = await Reader(db).GetLastAiringAsync(showId, CancellationToken.None);

            Assert.NotNull(lastAiring);
            Assert.Equal(4, lastAiring.Picks);
            Assert.Equal(2, lastAiring.Relaxed);
        }
    }

    // ---------------------------------------------------------------------
    // (b) T362 review HIGH-1's own validated case: another show's rows mid-window
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnotherShowsRowsMidWindowBreakTheRun(DatabaseFixture db)
    {
        // Given the target show airs, a DIFFERENT show airs in between (well inside the three-hour
        // gap threshold), then the target show airs again, When GetLastAiringAsync reads the target
        // show back.
        [Fact]
        public async Task TheRunBoundaryHoldsAtTheOtherShowsRows()
        {
            await db.ResetBoothLogAsync();
            const long targetShowId = 201;
            const long otherShowId = 202;
            var t0 = DateTimeOffset.UtcNow.AddHours(-2);

            // Target show's FIRST block — must NOT be folded into the count below.
            await InsertTrackStartedAsync(db, targetShowId, t0, rotationRelax: null);
            await InsertTrackStartedAsync(db, targetShowId, t0.AddMinutes(5), rotationRelax: null);

            // A different show's block, sitting BETWEEN the target show's two blocks — well inside
            // the 3-hour gap threshold on either side.
            await InsertTrackStartedAsync(db, otherShowId, t0.AddMinutes(10), rotationRelax: null);
            await InsertTrackStartedAsync(db, otherShowId, t0.AddMinutes(15), rotationRelax: null);

            // Target show's SECOND (most recent) block — the one GetLastAiringAsync must answer,
            // both rows relaxed (>0) so this fact also proves relaxed counts correctly here.
            await InsertTrackStartedAsync(db, targetShowId, t0.AddMinutes(20), rotationRelax: 1);
            await InsertTrackStartedAsync(db, targetShowId, t0.AddMinutes(25), rotationRelax: 1);

            var lastAiring = await Reader(db).GetLastAiringAsync(targetShowId, CancellationToken.None);

            // Then only the SECOND block counts — the other show's rows broke the run even though
            // every timestamp involved sits well inside three hours of its neighbors (the reviewer's
            // own verified picks=2/relaxed=2 outcome for this exact shape).
            Assert.NotNull(lastAiring);
            Assert.Equal(2, lastAiring.Picks);
            Assert.Equal(2, lastAiring.Relaxed);
        }
    }

    // ---------------------------------------------------------------------
    // (c) A row with no rotationRelax key counts as a pick, never as relaxed
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnUnstampedRowCountsAsAPickNotRelaxed(DatabaseFixture db)
    {
        // Given a show's own run mixes a stamped-relaxed row with an engine-initiated (unstamped)
        // one, When GetLastAiringAsync reads it back.
        [Fact]
        public async Task ThePickCountsButTheRelaxedCountDoesNot()
        {
            await db.ResetBoothLogAsync();
            const long showId = 301;
            var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);

            await InsertTrackStartedAsync(db, showId, t0, rotationRelax: null); // no pick column at all
            await InsertTrackStartedAsync(db, showId, t0.AddMinutes(3), rotationRelax: 1);

            var lastAiring = await Reader(db).GetLastAiringAsync(showId, CancellationToken.None);

            Assert.NotNull(lastAiring);
            Assert.Equal(2, lastAiring.Picks);
            Assert.Equal(1, lastAiring.Relaxed);
        }
    }

    // ---------------------------------------------------------------------
    // (d) GetEnvelopeCandidateCountAsync over a seeded pool, MaxPlays 0, scope excludes another library
    // ---------------------------------------------------------------------

    /// <summary>Story372_ThePoolHonoursTheRotationPredicate.cs's own InsertReadyAsync idiom
    /// (redefined here, not shared — see this file's own header).</summary>
    static async Task<long> InsertReadyAsync(MediaRepository repo, string path)
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);
        return id;
    }

    /// <summary>Story372_ThePoolHonoursTheRotationPredicate.cs's own SeedRotationAsync idiom.</summary>
    static async Task SeedRotationAsync(DatabaseFixture db, long mediaId, int playCount)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rotation (media_id, play_count) values (@mediaId, @playCount)",
            new { mediaId, playCount });
    }

    static readonly SegmentEnvelope UnconstrainedEnvelope =
        new(TimeOnly.MinValue, TimeOnly.MaxValue, [], EnergyRange.Unconstrained);

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheCountHonoursMaxPlaysAndScope(DatabaseFixture db)
    {
        // Given a MaxPlays 0 rule, 6 never-aired + 4 aired rows in the scoped library, and 3 more
        // never-aired rows in a SECOND, out-of-scope library, When the count is queried scoped to
        // just the first library.
        [Fact]
        public async Task OnlyTheSixInScopeNeverAiredRowsAreCounted()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            for (var i = 0; i < 6; i++)
            {
                var id = await InsertReadyAsync(repo, $"/rotation/count-in-scope-never-{i}.flac");
                _ = id; // never aired — no library.media_rotation row at all
            }

            for (var i = 0; i < 4; i++)
            {
                var id = await InsertReadyAsync(repo, $"/rotation/count-in-scope-aired-{i}.flac");
                await SeedRotationAsync(db, id, playCount: 1);
            }

            // A second library, out of the scope this fact queries — its own never-aired rows must
            // never inflate the count (StoryF3_BulkEligibilityByFilter.cs's own second-library idiom).
            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            var otherLibraryId = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.library (name) values ('Story373OutOfScopeLib') returning id");

            for (var i = 0; i < 3; i++)
            {
                var path = $"/rotation/count-out-of-scope-{i}.flac";
                var outOfScopeId = await connSetup.ExecuteScalarAsync<long>(
                    "insert into library.media (path, format, size_bytes, mtime, library_id) " +
                    "values (@path, 'flac', 1, @mtime, @otherLibraryId) returning id",
                    new { path, mtime = Harness.Mtime, otherLibraryId });
                await repo.WriteEnrichmentAsync(outOfScopeId, Harness.ReadyResult(measurable: true), CancellationToken.None);
            }

            var catalog = (IMediaCatalog)repo;
            var scope = new LibraryScope([1L]);
            var envelope = UnconstrainedEnvelope with { Rotation = new RotationPredicate(MaxPlays: 0) };

            var eligible = await catalog.GetEnvelopeCandidateCountAsync(scope, envelope, CancellationToken.None);

            Assert.Equal(6, eligible);
        }
    }
}
