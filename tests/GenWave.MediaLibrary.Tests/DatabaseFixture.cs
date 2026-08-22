using System.Collections.Frozen;
using System.Diagnostics;
using Dapper;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests;

/// <summary>
/// Brings up a disposable Postgres (db-compose.yaml) initialised with the production
/// <c>db/01-library.sh</c>, so the integration tests run against the real <c>library</c> schema and
/// the <c>library_svc</c> role. On-demand: requires Docker; tears the database down (<c>down -v</c>)
/// when the test collection finishes. Each test calls <see cref="ResetAsync"/> for a clean catalog.
/// <para>
/// gh-#569: the compose project name and host port are both derived per instance rather than fixed,
/// so two concurrent runs (two checkouts, or CI + local) get isolated containers instead of the
/// second run's <c>up</c>/<c>down</c> tearing the first run's Postgres out from under it. The host
/// port is whatever Docker assigns to the container's unpublished-to-a-fixed-port <c>5432</c> (see
/// db-compose.yaml), discovered via <c>docker compose port</c> after <c>up</c>.
/// </para>
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    readonly string project = $"genwave-libtest-{Guid.NewGuid():N}"[..24];

    public string ConnectionString { get; private set; } = "";

    /// <summary>
    /// Connects as station_svc (Search Path=station) rather than library_svc — the two roles are
    /// deliberately isolated from each other's schema (no cross-schema grants), so
    /// <see cref="PersonaRepository"/>-shaped tests need this data source, not <see cref="DataSource"/>.
    /// </summary>
    public string StationConnectionString { get; private set; } = "";

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public NpgsqlDataSource StationDataSource { get; private set; } = null!;

    /// <summary>
    /// (data_type, is_nullable, column_default) for every column of the <c>station</c> and
    /// <c>library</c> schemas, snapshotted in <see cref="InitializeAsync"/> right after
    /// <see cref="WaitForSchemaAsync"/> — the one instant that is the PURE db/01+db/06 fresh-init
    /// world, since <c>db-compose.yaml</c> mounts only those two files as Postgres init scripts. No
    /// test class has run yet, so nothing here can have been shaped by db/35 (or any other in-place
    /// migration script) rather than by db/06's own CREATE. Story305_ShowRepository.cs's fresh-init
    /// facts assert against this snapshot instead of re-running a migration script, which is the only
    /// way a dropped db/06 mirror of a db/35 column can actually turn a fact red.
    /// </summary>
    public IReadOnlyDictionary<(string Schema, string Table, string Column), (string DataType, string IsNullable, string? ColumnDefault)> InitialSchema
    { get; private set; } = new Dictionary<(string, string, string), (string, string, string?)>();

    /// <summary>
    /// The UNIQUE constraint names (<c>pg_constraint.conname</c>, <c>contype = 'u'</c>) present on
    /// each <c>station</c>-schema table at the same fresh-init instant <see cref="InitialSchema"/>
    /// captures, keyed by bare table name. A table can carry more than one UNIQUE constraint (e.g.
    /// <c>station.persona</c> has both <c>name</c> and <c>slug</c>), hence a list per table rather
    /// than a single name.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> InitialUniqueConstraints
    { get; private set; } = new Dictionary<string, IReadOnlyList<string>>();

    string composeFile = "";

    public async Task InitializeAsync()
    {
        // Production sets this in AddMediaLibrary; these tests construct the repository directly.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Same reason (PLAN T258 review MF2): production registers DateOnlyTypeHandler in
        // AddMediaLibrary too — SpecialsRepository (station.schedule_special.on_date) is constructed
        // directly here, never through that DI extension.
        SqlMapper.AddTypeHandler(DateOnlyTypeHandler.Instance);

        // Same reason again (SPEC F143, STORY-357, PLAN T337): production registers
        // AnnouncementStateTypeHandler in AddAnnouncementStore — Harness.AnnouncementRepo constructs
        // AnnouncementRepository directly here, never through that DI extension either.
        SqlMapper.AddTypeHandler(AnnouncementStateTypeHandler.Instance);

        composeFile = LocateComposeFile(out var repoRoot);
        RepoRoot = repoRoot;
        Compose("up", "-d", "--wait");

        var port = DiscoverHostPort("testdb", 5432);
        ConnectionString = $"Host=localhost;Port={port};Database=genwave;Username=library_svc;Password=libtest;Search Path=library";
        StationConnectionString = $"Host=localhost;Port={port};Database=genwave;Username=station_svc;Password=stationtest;Search Path=station";

        DataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        StationDataSource = new NpgsqlDataSourceBuilder(StationConnectionString).Build();
        await WaitForSchemaAsync();
        await SnapshotInitialSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null) await DataSource.DisposeAsync();
        if (StationDataSource is not null) await StationDataSource.DisposeAsync();
        try { Compose("down", "-v"); } catch { /* best-effort teardown */ }
    }

    /// <summary>
    /// Truncate the catalog and reset its identity so ids are predictable per test. CASCADE is
    /// required (not optional) once <c>library.media_rating</c> exists (STORY-109): it is a 1:1
    /// extension table whose PK is a FK into <c>library.media</c>, and Postgres refuses to TRUNCATE a
    /// table with a live FK reference from another table unless that table is included — CASCADE
    /// truncates it too, which is exactly the "no orphaned rating rows" behavior tests want.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var conn = await DataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table library.media restart identity cascade";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.persona</c> and reset its identity (STORY-118). The library-schema
    /// <see cref="ResetAsync"/> never reaches the station schema (library_svc has no grants there),
    /// so persona tests get their own reset, over <see cref="StationDataSource"/>. CASCADE (STORY-192):
    /// once <c>station.persona_memory</c> exists (SPEC F71.1) its FK into <c>station.persona</c> makes
    /// Postgres refuse a plain TRUNCATE regardless of row count — same reason <see cref="ResetAsync"/>
    /// itself needed CASCADE once <c>library.media_rating</c> existed.
    ///
    /// STORY-215: also sweeps <c>station.booth_log</c> — TRUNCATE CASCADE follows every FK into the
    /// truncated table regardless of its <c>ON DELETE</c> action, so <c>booth_log.persona_id</c>'s
    /// <c>ON DELETE SET NULL</c> (F84.6) does not exempt it here the way it does a real DELETE. No
    /// existing caller asserts booth-log survival across this reset; <see cref="ResetBoothLogAsync"/>
    /// is the explicit reset for tests that care about booth-log content.
    /// </summary>
    public async Task ResetStationAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.persona restart identity cascade";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.booth_log</c> and reset its identity (STORY-195). CASCADE (STORY-215,
    /// PLAN T70): once <c>station.persona_taste_thumb</c> exists (SPEC F84.5) its FK into
    /// <c>station.booth_log</c> makes Postgres refuse a plain TRUNCATE regardless of row count — same
    /// reason <see cref="ResetStationAsync"/> itself needed CASCADE once <c>station.persona_memory</c>
    /// existed.
    /// </summary>
    public async Task ResetBoothLogAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.booth_log restart identity cascade";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.request</c> and reset its identity (SPEC F87, STORY-224, PLAN T86). No
    /// FK references this table — <c>matched_media_id</c> is a bare <c>bigint</c> with no FK (the
    /// <c>booth_log.media_id</c> precedent) — so no CASCADE is required yet, unlike
    /// <see cref="ResetStationAsync"/>/<see cref="ResetBoothLogAsync"/>, both of which needed it only
    /// once a later table's FK into them forced Postgres's hand.
    /// </summary>
    public async Task ResetRequestAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.request restart identity";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.segment_schedule</c> and reset its identity (SPEC F91.1, STORY-240, PLAN
    /// T118). No FK references this table — <c>persona_id</c> points OUT to <c>station.persona</c>,
    /// not the other way around — so no CASCADE is required, the same reasoning
    /// <see cref="ResetRequestAsync"/>'s own remarks give.
    /// </summary>
    public async Task ResetScheduleAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.segment_schedule restart identity";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.theme</c> and reset its identity (SPEC F103.7, STORY-271, PLAN T182). No
    /// FK references this table — <c>slug</c> is a standalone unique key, nothing points into or out
    /// of it — so no CASCADE is required, the same reasoning <see cref="ResetScheduleAsync"/>'s own
    /// remarks give.
    /// </summary>
    public async Task ResetThemeAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.theme restart identity";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.font_pack</c> and reset its identity (SPEC F104, STORY-282, PLAN T198).
    /// CASCADE (mirrors <see cref="ResetStationAsync"/>'s own remarks): <c>station.font_pack_face</c>'s
    /// FK into this table makes Postgres refuse a plain TRUNCATE regardless of row count, and CASCADE
    /// sweeps every face row along with its owning pack — there is nothing to reset separately.
    /// </summary>
    public async Task ResetFontPackAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.font_pack restart identity cascade";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.show</c> and reset its identity (SPEC F115.1, STORY-305, PLAN T239).
    /// CASCADE (mirrors <see cref="ResetStationAsync"/>'s own remarks): <c>station.segment_schedule</c>'s
    /// <c>show_id</c> FK (db/06, SPEC F114) — <c>ON DELETE RESTRICT</c> though it is — still makes
    /// Postgres refuse a plain TRUNCATE, since TRUNCATE's own FK check is stricter than any single
    /// row's <c>ON DELETE</c> action; CASCADE follows it and sweeps that table's rows along with
    /// <c>station.show</c>'s, same as it already does for the persona-referencing tables
    /// <see cref="ResetStationAsync"/> truncates. <c>station.booth_log.show_id</c>/
    /// <c>library.media.show_id</c> carry no FK (db/35 — history/imaging must outlive the entity), so
    /// neither is touched by this reset.
    /// </summary>
    public async Task ResetShowAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.show restart identity cascade";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.schedule_special</c> and reset its identity (SPEC F120.1, STORY-317, PLAN
    /// T258). No FK references this table — <c>persona_id</c>/<c>show_id</c> point OUT to
    /// <c>station.persona</c>/<c>station.show</c>, not the other way around — so no CASCADE is
    /// required, the same reasoning <see cref="ResetScheduleAsync"/>'s own remarks give.
    /// </summary>
    public async Task ResetSpecialsAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.schedule_special restart identity";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.settings</c> (gh-#406 slice 3, STORY-042's original table). No identity
    /// to restart — <c>key</c> is a bare <c>text</c> primary key, no <c>serial</c>/sequence backs
    /// it — and no FK references this table, the same "no CASCADE required" reasoning
    /// <see cref="ResetRequestAsync"/>'s own remarks give.
    /// </summary>
    public async Task ResetSettingsAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.settings";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.announcement</c> and reset its identity (SPEC F143, STORY-357, PLAN T337).
    /// No FK references this table — it is a leaf table, nothing points into or out of it — so no
    /// CASCADE is required, the same reasoning <see cref="ResetRequestAsync"/>'s own remarks give.
    /// </summary>
    public async Task ResetAnnouncementAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.announcement restart identity";
        await cmd.ExecuteNonQueryAsync();
    }

    async Task WaitForSchemaAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var conn = await DataSource.OpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "select 1 from library.media limit 0";
                await cmd.ExecuteScalarAsync();
                return;
            }
            catch (NpgsqlException)
            {
                await Task.Delay(1000);
            }
        }

        throw new InvalidOperationException("library schema not ready on the test database");
    }

    /// <summary>
    /// Populates <see cref="InitialSchema"/> and <see cref="InitialUniqueConstraints"/> — must run
    /// only once, immediately after <see cref="WaitForSchemaAsync"/> and before any test class gets a
    /// chance to run a migration script or otherwise mutate the schema (see those properties' own
    /// remarks for why that instant matters). Reads the station schema over
    /// <see cref="StationDataSource"/> and the library schema over <see cref="DataSource"/> — the two
    /// roles have no cross-schema grants, the same reason <c>QueryColumnAsync</c>/
    /// <c>QueryLibraryMediaColumnAsync</c> in Story305_ShowRepository.cs split the same way.
    /// </summary>
    async Task SnapshotInitialSchemaAsync()
    {
        var schema = new Dictionary<(string, string, string), (string, string, string?)>();

        await AddColumnsAsync(StationDataSource, "station");
        await AddColumnsAsync(DataSource, "library");

        async Task AddColumnsAsync(NpgsqlDataSource dataSource, string schemaName)
        {
            await using var conn = await dataSource.OpenConnectionAsync();
            var rows = await conn.QueryAsync<(string TableName, string ColumnName, string DataType, string IsNullable, string? ColumnDefault)>(
                """
                select table_name, column_name, data_type, is_nullable, column_default
                from information_schema.columns
                where table_schema = @schemaName
                """,
                new { schemaName });
            foreach (var row in rows)
                schema[(schemaName, row.TableName, row.ColumnName)] = (row.DataType, row.IsNullable, row.ColumnDefault);
        }

        // Frozen so no spec can mutate the shared snapshot via a cast back to a mutable dictionary type.
        InitialSchema = schema.ToFrozenDictionary();

        var uniqueConstraints = new Dictionary<string, List<string>>();
        await using (var conn = await StationDataSource.OpenConnectionAsync())
        {
            var rows = await conn.QueryAsync<(string TableName, string ConstraintName)>(
                """
                select c.relname as table_name, con.conname as constraint_name
                from pg_constraint con
                join pg_class c on c.oid = con.conrelid
                join pg_namespace n on n.oid = c.relnamespace
                where con.contype = 'u' and n.nspname = 'station'
                """);
            foreach (var row in rows)
            {
                if (!uniqueConstraints.TryGetValue(row.TableName, out var names))
                    uniqueConstraints[row.TableName] = names = [];
                names.Add(row.ConstraintName);
            }
        }

        // Each value is a defensive copy (not the live List) so no spec can mutate the shared snapshot.
        InitialUniqueConstraints = uniqueConstraints.ToDictionary(
            entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value.ToArray());
    }

    /// <summary>
    /// Absolute path to the repository root (the directory containing GenWave.sln).
    /// Populated during <see cref="InitializeAsync"/>; use to resolve files like <c>db/*.sh</c>.
    /// </summary>
    public string RepoRoot { get; private set; } = "";

    /// <summary>
    /// Pipes <paramref name="hostScriptPath"/> to <c>bash -s</c> inside the <c>testdb</c>
    /// compose service. Equivalent to: <c>docker compose … exec -T testdb bash -s &lt; script</c>.
    /// The Postgres container image includes bash + psql and has POSTGRES_USER / POSTGRES_DB set,
    /// so shell scripts that call psql work without modification.
    /// </summary>
    public void RunFileInContainer(string hostScriptPath)
    {
        var args = new List<string> { "compose", "-p", project, "-f", composeFile, "exec", "-T", "testdb", "bash", "-s" };

        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start docker compose exec");

        // Stream the script file into bash's stdin, then close so bash sees EOF.
        using (var scriptStream = File.OpenRead(hostScriptPath))
            scriptStream.CopyTo(p.StandardInput.BaseStream);
        p.StandardInput.Close();

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();

        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"Script {hostScriptPath} failed in container (exit {p.ExitCode}):\n{stderrTask.Result}\n{stdoutTask.Result}");
    }

    void Compose(params string[] verbAndArgs)
    {
        var args = new List<string> { "compose", "-p", project, "-f", composeFile };
        args.AddRange(verbAndArgs);
        Run("docker", args);
    }

    /// <summary>
    /// Asks Docker which host port it assigned to <paramref name="containerPort"/> on
    /// <paramref name="service"/> (gh-#569: db-compose.yaml publishes that port without pinning a
    /// host side, so Docker picks a free one per run). Expects <c>docker compose port</c>'s single-line
    /// <c>HOST_IP:HOST_PORT</c> output.
    /// </summary>
    int DiscoverHostPort(string service, int containerPort)
    {
        var output = RunCapture("docker", ["compose", "-p", project, "-f", composeFile, "port", service, containerPort.ToString()]).Trim();
        var lastColon = output.LastIndexOf(':');
        if (lastColon < 0 || !int.TryParse(output[(lastColon + 1)..], out var hostPort))
            throw new InvalidOperationException($"could not parse a host port from 'docker compose port' output: '{output}'");
        return hostPort;
    }

    static void Run(string file, IReadOnlyList<string> args) => RunCapture(file, args);

    static string RunCapture(string file, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {file}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {string.Join(' ', args)} failed:\n{stderr.Result}{stdout.Result}");
        return stdout.Result;
    }

    static string LocateComposeFile(out string repoRoot)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        repoRoot = dir.FullName;
        return Path.Combine(dir.FullName, "tests", "GenWave.MediaLibrary.Tests", "db-compose.yaml");
    }
}
