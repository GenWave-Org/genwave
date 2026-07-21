using System.Diagnostics;
using Dapper;
using Npgsql;

namespace GenWave.MediaLibrary.Tests;

/// <summary>
/// Brings up a disposable Postgres (db-compose.yaml) initialised with the production
/// <c>db/01-library.sh</c>, so the integration tests run against the real <c>library</c> schema and
/// the <c>library_svc</c> role. On-demand: requires Docker; tears the database down (<c>down -v</c>)
/// when the test collection finishes. Each test calls <see cref="ResetAsync"/> for a clean catalog.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    const string Project = "genwave-libtest";

    public string ConnectionString { get; } =
        "Host=localhost;Port=55433;Database=genwave;Username=library_svc;Password=libtest;Search Path=library";

    /// <summary>
    /// Connects as station_svc (Search Path=station) rather than library_svc — the two roles are
    /// deliberately isolated from each other's schema (no cross-schema grants), so
    /// <see cref="PersonaRepository"/>-shaped tests need this data source, not <see cref="DataSource"/>.
    /// </summary>
    public string StationConnectionString { get; } =
        "Host=localhost;Port=55433;Database=genwave;Username=station_svc;Password=stationtest;Search Path=station";

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public NpgsqlDataSource StationDataSource { get; private set; } = null!;

    string composeFile = "";

    public async Task InitializeAsync()
    {
        // Production sets this in AddMediaLibrary; these tests construct the repository directly.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        composeFile = LocateComposeFile(out var repoRoot);
        RepoRoot = repoRoot;
        Compose("up", "-d", "--wait");

        DataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        StationDataSource = new NpgsqlDataSourceBuilder(StationConnectionString).Build();
        await WaitForSchemaAsync();
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
    /// </summary>
    public async Task ResetStationAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.persona restart identity cascade";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncate <c>station.booth_log</c> and reset its identity (STORY-195). No CASCADE needed —
    /// unlike <see cref="ResetStationAsync"/>'s target, nothing has a FK into this table.
    /// </summary>
    public async Task ResetBoothLogAsync()
    {
        await using var conn = await StationDataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "truncate table station.booth_log restart identity";
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
        var args = new List<string> { "compose", "-p", Project, "-f", composeFile, "exec", "-T", "testdb", "bash", "-s" };

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
        var args = new List<string> { "compose", "-p", Project, "-f", composeFile };
        args.AddRange(verbAndArgs);
        Run("docker", args);
    }

    static void Run(string file, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {file}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {string.Join(' ', args)} failed:\n{stderr.Result}{stdout.Result}");
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
