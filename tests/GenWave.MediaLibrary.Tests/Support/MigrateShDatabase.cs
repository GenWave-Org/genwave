using GenWave.Host.Tests.Support;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Support;

/// <summary>
/// Brings up a disposable Postgres whose compose service is literally named <c>db</c> — the one thing
/// <c>db-compose.yaml</c>/<see cref="DatabaseFixture"/> (service <c>testdb</c>) cannot host — so
/// Story484_SchemaJournal.cs can drive the REAL, unmodified <c>./migrate.sh</c> end to end (STORY-484,
/// PLAN T591): that script hardcodes <c>compose ps -q db</c> / <c>compose exec -T db ...</c>, matching
/// production's own <c>compose.yaml</c>. See <c>migrate-compose.yaml</c>'s own header for why this
/// couldn't just be a second service on the existing fixture file.
/// <para>
/// Same shape as <see cref="DatabaseFixture"/>/<c>EphemeralStationDatabase</c>: a random compose
/// project name and an OS-assigned host port per instance (gh-#569), so concurrent runs don't tear
/// each other's containers down; <c>down -v</c> on <see cref="DisposeAsync"/>.
/// </para>
/// </summary>
internal sealed class MigrateShDatabase : IAsyncDisposable
{
    readonly string project;
    readonly string composeFile;
    bool disposed;

    /// <summary>Connects as station_svc (Search Path=station) — the role migrate.sh's own journal
    /// upsert runs as (<c>SET ROLE station_svc</c>).</summary>
    public string StationConnectionString { get; }

    MigrateShDatabase(string project, string composeFile, string stationConnectionString)
    {
        this.project = project;
        this.composeFile = composeFile;
        StationConnectionString = stationConnectionString;
    }

    public static async Task<MigrateShDatabase> StartAsync()
    {
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
        var composeFile = Path.Combine(repoRoot, "tests", "GenWave.MediaLibrary.Tests", "migrate-compose.yaml");
        var project = $"genwave-migratetest-{Guid.NewGuid():N}"[..24];

        TestNetwork.Ensure();
        try
        {
            ComposeProcess.Compose(project, composeFile, "up", "-d", "--wait");

            var port = ComposeProcess.DiscoverHostPort(project, composeFile, "db", 5432);
            var station =
                $"Host=localhost;Port={port};Database=genwave;Username=station_svc;Password=stationtest;Search Path=station";

            var database = new MigrateShDatabase(project, composeFile, station);
            await database.WaitForSchemaAsync();
            return database;
        }
        catch
        {
            // Startup failed partway (compose up, port discovery, or the schema wait) — leave nothing
            // running behind a thrown exception. Teardown failures here are logged, not swallowed, but
            // never replace the real startup failure being propagated.
            try
            {
                ComposeProcess.Compose(project, composeFile, "down", "-v");
            }
            catch (InvalidOperationException teardownEx)
            {
                Console.Error.WriteLine(
                    $"MigrateShDatabase.StartAsync: teardown after a failed start also failed: {teardownEx.Message}");
            }

            throw;
        }
    }

    async Task WaitForSchemaAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(StationConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "select 1 from station.settings limit 0";
                await cmd.ExecuteScalarAsync();
                return;
            }
            catch (NpgsqlException)
            {
                await Task.Delay(1000);
            }
        }

        throw new InvalidOperationException("station schema not ready on the migrate.sh test database");
    }

    /// <summary>
    /// Runs migrate.sh — the real repo copy, or a scratch copy at <paramref name="migrateShPath"/> (AC9's
    /// planted-failure scenario) — against THIS instance's own compose project, via
    /// <see cref="ScriptProcess"/>'s sanitized-environment runner with a real <c>docker</c> on PATH.
    /// <c>COMPOSE_PROJECT_NAME</c> is what points migrate.sh's own plain <c>docker compose -f
    /// migrate-compose.yaml ...</c> calls at the already-running project <see cref="StartAsync"/>
    /// brought up, exactly as <c>-p</c> does for this type's own <see cref="ComposeProcess"/> calls.
    /// </summary>
    public (int ExitCode, string StdOut, string StdErr) RunMigrateSh(
        string? gwVersion = null, string? migrateShPath = null, bool keepGoing = false)
    {
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
        var script = migrateShPath ?? Path.Combine(repoRoot, "migrate.sh");

        var extraEnv = new Dictionary<string, string> { ["COMPOSE_PROJECT_NAME"] = project };
        if (gwVersion is not null)
            extraEnv["GW_VERSION"] = gwVersion;

        // journal_migration() derives $script via pure bash parameter expansion (${migration##*/}) —
        // no external "basename" process, so ScriptProcess's sanitized bin dir needs only "docker".
        var bin = ScriptProcess.MakeBinDir("docker");
        var args = new List<string> { "-f", composeFile };
        if (keepGoing)
            args.Add("--keep-going");
        return ScriptProcess.Run(script, bin, extraEnv: extraEnv, args: args.ToArray());
    }

    /// <summary>
    /// Drops station.schema_migration — db/06's own fresh-init mirror (gh-#618) already creates it on
    /// every instance <see cref="StartAsync"/> provisions, so a scenario simulating an upgrading
    /// pre-F211 box (no journal table yet) removes it explicitly rather than starting from an
    /// untouched fresh boot.
    /// </summary>
    public async Task DropSchemaJournalAsync()
    {
        await using var conn = new NpgsqlConnection(StationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "drop table if exists station.schema_migration";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>(column name, data type, is_nullable) for station.schema_migration, ordinal-ordered — AC1's
    /// table-shape check.</summary>
    public async Task<IReadOnlyList<(string Name, string DataType, string IsNullable)>> ReadJournalColumnsAsync()
    {
        await using var conn = new NpgsqlConnection(StationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select column_name, data_type, is_nullable
            from information_schema.columns
            where table_schema = 'station' and table_name = 'schema_migration'
            order by ordinal_position
            """;
        await using var reader = await cmd.ExecuteReaderAsync();

        var columns = new List<(string, string, string)>();
        while (await reader.ReadAsync())
            columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return columns;
    }

    public async Task<IReadOnlyList<(string Script, DateTimeOffset AppliedAt, string? AppVersion)>> ReadJournalAsync()
    {
        await using var conn = new NpgsqlConnection(StationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select script, applied_at, app_version from station.schema_migration order by script";
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(string, DateTimeOffset, string?)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        try
        {
            ComposeProcess.Compose(project, composeFile, "down", "-v");
        }
        catch (InvalidOperationException ex)
        {
            // Best-effort teardown: the compose project (and any Postgres container it left running)
            // will still get swept by the next full `docker compose down` — but a failure here should
            // be visible in test output, not silently discarded.
            Console.Error.WriteLine($"MigrateShDatabase: 'docker compose -p {project} down -v' failed: {ex.Message}");
        }

        return ValueTask.CompletedTask;
    }
}
