// STORY-375 — The push guard's finding survives a reconcile (SPEC F153.2-F153.4 · PLAN T373 ·
// ORCHESTRATOR ruling)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. Pins
// the ORCHESTRATOR ruling's flap guard directly against RotFindingRepository (no Host, no
// MediaExistencePushGuard — that "does the push honestly decline and report" half is Story375's
// own Host.Tests file): a push_missing finding IDeadFileReporter opens on a row whose
// library.media state is still 'ready' (the scan has not judged the row yet) must SURVIVE
// ReconcileDeadFilesAsync while younger than the miss grace (a) — otherwise T372's own state-based
// resolve half would close it on the very next Gardener tick, then IDeadFileReporter would re-open
// it on the very next declined push: a flap. Once the SAME finding ages past that SAME grace with
// the row STILL ready (b), it resolves — the state predicate decides from there, exactly as it did
// before push_missing existed.

using Dapper;
using GenWave.MediaLibrary.Garden;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRotFindingFlapGuard
{
    static RotFindingRepository Repo(DatabaseFixture db) => new(db.DataSource);

    static async Task<long> InsertReadyMediaRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, library_id)
            values (@path, 'flac', 1024, now(), 'ready', 1)
            returning id
            """,
            new { path });
    }

    static async Task<string?> ReadFindingStateAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "select state::text from library.rot_finding where media_id = @mediaId and kind = 'dead_file'",
            new { mediaId });
    }

    static async Task BackdateOpenedAtAsync(DatabaseFixture db, long mediaId, TimeSpan age)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.rot_finding set opened_at = now() - @age where media_id = @mediaId and kind = 'dead_file'",
            new { age, mediaId });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the flap guard holds a fresh finding open, then lets it resolve once stale
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAFreshFindingOnAStillReadyRow(DatabaseFixture db)
    {
        // Given a push_missing finding just opened on a row whose state is still ready, When the
        // dead_file reconcile runs with a grace window the finding has not yet cleared.
        [Fact]
        public async Task TheFindingSurvivesTheReconcile()
        {
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "41-gardener-migration.sh"));
            await db.ResetAsync();

            var repo = Repo(db);
            var mediaId = await InsertReadyMediaRowAsync(db, "/test/t373-flap-fresh.flac");
            await repo.OpenDeadFileAsync(mediaId, "push_missing", CancellationToken.None);

            await repo.ReconcileDeadFilesAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

            Assert.Equal("open", await ReadFindingStateAsync(db, mediaId));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnAgedFindingOnAStillReadyRow(DatabaseFixture db)
    {
        // Given that same finding backdated past the grace, the row still ready, When the
        // dead_file reconcile runs again.
        [Fact]
        public async Task TheFindingResolves()
        {
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "41-gardener-migration.sh"));
            await db.ResetAsync();

            var repo = Repo(db);
            var mediaId = await InsertReadyMediaRowAsync(db, "/test/t373-flap-aged.flac");
            await repo.OpenDeadFileAsync(mediaId, "push_missing", CancellationToken.None);
            await BackdateOpenedAtAsync(db, mediaId, TimeSpan.FromSeconds(5));

            await repo.ReconcileDeadFilesAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Equal("resolved", await ReadFindingStateAsync(db, mediaId));
        }
    }
}
