// STORY-557 — Upload a clip, get a voice, the schema half (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. T755 activated the four facts. Postgres-backed ones are Category=Integration
// (shared DatabaseFixture: db/01 + db/06 fresh-init, then db/53 replayed in place).

using Dapper;
using GenWave.Host.Tests.Support;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureVoiceClipMirror
{
    const string Db53 = "53-voice-clip-migration.sh";

    const string EngineChecksSql =
        """
        select count(*)::integer from pg_constraint
        where conrelid = 'station.voice_pack'::regclass and contype = 'c'
          and pg_get_constraintdef(oid) ilike '%engine%'
        """;

    // db/53 and the fresh-init mirror
    public sealed class ScenarioFreshInitAndMigrationAgree
    {
        [Fact]
        public void TheTableTextIsEqual()
        {
            var root = RepoRootLocator.Find(AppContext.BaseDirectory);
            var fresh = MirroredSqlBlock.Read(Path.Combine(root, "db", "06-station-settings-migration.sh"), "f219-voice-clip");
            var upgrade = MirroredSqlBlock.Read(Path.Combine(root, "db", Db53), "f219-voice-clip");

            Assert.Equal(fresh, upgrade);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheUpgradeOnAnExistingDatabase : IAsyncLifetime
    {
        readonly DatabaseFixture db;
        int engineChecks;

        public ScenarioTheUpgradeOnAnExistingDatabase(DatabaseFixture db) => this.db = db;

        public async Task InitializeAsync()
        {
            // Given a database as db/45 left it: the closed-set engine check present, the table absent,
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                """
                drop table if exists station.voice_clip;
                delete from station.voice_pack where engine <> 'kokoro';
                alter table station.voice_pack drop constraint if exists voice_pack_engine_check;
                alter table station.voice_pack add check (engine in ('kokoro')); -- db/45's own text, Postgres names it
                """);

            // When db/53 runs twice (RunFileInContainer throws on a nonzero exit),
            var script = Path.Combine(db.RepoRoot, "db", Db53);
            db.RunFileInContainer(script);
            db.RunFileInContainer(script);

            engineChecks = await conn.QuerySingleAsync<int>(EngineChecksSql);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheMigrationRunsTwiceCleanlyAndDropsTheEngineCheck() => Assert.Equal(0, engineChecks);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheClipTableGuardsItsDuration(DatabaseFixture db)
    {
        [Fact]
        public async Task ADurationOutsideFiveToTwentySecondsIsRefused()
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // Given a clip one millisecond short of five seconds, the schema refuses it (check violation).
            var failure = await Record.ExceptionAsync(() => conn.ExecuteAsync(
                "insert into station.voice_clip (slug, label, file_name, duration_ms, source) values ('too-short', 'Too short', 'too-short.wav', 4999, 'upload')"));

            var refused = Assert.IsType<PostgresException>(failure);
            Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheVoicePackEngineIsOpen(DatabaseFixture db)
    {
        [Fact]
        public async Task TheVoicePackEngineCheckIsGone()
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // No check constraint on station.voice_pack mentions engine (VoicePackController validates it now).
            var engineChecks = await conn.QuerySingleAsync<int>(EngineChecksSql);

            Assert.Equal(0, engineChecks);
        }
    }
}
