// STORY-566 — Every talk check is recorded, the schema half (SPEC F220 · PLAN T778)
//
// BDD specification — xUnit. T778 activated these facts. Postgres-backed ones are Category=Integration
// (shared DatabaseFixture: db/01 + db/06 fresh-init, then db/54 replayed in place).

using Dapper;
using GenWave.Host.Tests.Support;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkCheckMirror
{
    const string Db54 = "54-talk-rules-migration.sh";

    const string TablesSql =
        """
        select count(*)::integer from pg_class c join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = 'station' and c.relname in ('talk_rule_switch', 'talk_check')
        """;

    // db/54 and the fresh-init mirror
    public sealed class ScenarioFreshInitAndMigrationAgree
    {
        [Fact]
        public void TheTableTextIsEqual()
        {
            var root = RepoRootLocator.Find(AppContext.BaseDirectory);
            var fresh = MirroredSqlBlock.Read(Path.Combine(root, "db", "06-station-settings-migration.sh"), "f220-talk-rules");
            var upgrade = MirroredSqlBlock.Read(Path.Combine(root, "db", Db54), "f220-talk-rules");

            Assert.Equal(fresh, upgrade);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheUpgradeOnAnExistingDatabase : IAsyncLifetime
    {
        readonly DatabaseFixture db;
        int tables;

        public ScenarioTheUpgradeOnAnExistingDatabase(DatabaseFixture db) => this.db = db;

        public async Task InitializeAsync()
        {
            // Given a database as db/53 left it: neither table present,
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("drop table if exists station.talk_check; drop table if exists station.talk_rule_switch;");

            // When db/54 runs twice (RunFileInContainer throws on a nonzero exit),
            var script = Path.Combine(db.RepoRoot, "db", Db54);
            db.RunFileInContainer(script);
            db.RunFileInContainer(script);

            tables = await conn.QuerySingleAsync<int>(TablesSql);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheMigrationRunsTwiceCleanlyAndCreatesBothTables() => Assert.Equal(2, tables);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwitchTableGuardsItsKey(DatabaseFixture db)
    {
        [Fact]
        public async Task ARuleIdThatIsNotASlugIsRefused()
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // Given an upper-case rule id, the schema refuses it (check violation).
            var failure = await Record.ExceptionAsync(() => conn.ExecuteAsync(
                "insert into station.talk_rule_switch (rule_id, talk_kind, enabled) values ('Bad_Rule', 'chat', true)"));

            var refused = Assert.IsType<PostgresException>(failure);
            Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheCheckTableGuardsItsAttempt(DatabaseFixture db)
    {
        [Fact]
        public async Task AThirdAttemptIsRefused()
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // Given attempt 3, the schema refuses it (check violation).
            var failure = await Record.ExceptionAsync(() => conn.ExecuteAsync(
                """
                insert into station.talk_check (talk_kind, attempt, mode, raw_reply, snapshot, verdicts, outcome)
                values ('dj-line', 3, 'shadow', 'hi', '{}'::jsonb, '{}'::jsonb, 'accepted')
                """));

            var refused = Assert.IsType<PostgresException>(failure);
            Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        }
    }
}
