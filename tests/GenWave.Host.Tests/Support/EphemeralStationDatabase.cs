// Extracted from Story345_PaWireProof.cs and Story366_SensorWorksWithAdminOff.cs (T351 review round
// 2, finding 6): both files carried their own verbatim ~115-line "bring up a disposable Postgres via
// the shared db-compose.yaml, discover its OS-assigned host port, wait for the station schema" —
// byte-identical except the compose project-name PREFIX and Story345's own one extra
// ReadAnnouncementSourceAsync query method. A `file`-scoped type genuinely cannot cross files, but a
// normal internal type in the test project's own Support/ folder can (mirrors LlmCompletionsStub.cs's
// and CrosstalkWorkerHarness.cs's own identical precedent). This is that shared home: both spec files
// now consume it via a thin, file-scoped subclass that supplies only its own compose project-name
// prefix (and, for Story345, its one extra query method) — never a second copy of the compose/port/
// wait machinery to keep in sync.

using System.Diagnostics;
using Npgsql;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Brings up a disposable Postgres (the SAME db-compose.yaml GenWave.MediaLibrary.Tests/DatabaseFixture.cs
/// already uses — the single source of truth: db/01-library.sh + db/06-station-settings-migration.sh,
/// where db/06 mirrors every station-schema migration through db/40's own announcements table, proven
/// by that project's ScenarioMigrationConvergence) rather than a second copy of that compose file. A
/// unique compose project name + an OS-assigned host port per instance (gh-#569/#602's own lesson,
/// mirrored from DatabaseFixture/KokoroFixture) means every caller's own ephemeral Postgres is fully
/// isolated — safe under xUnit's default cross-class/cross-collection parallelization, since no two
/// instances ever share a database.
///
/// <b>Base class, not a sealed reusable type directly.</b> Each caller (<c>TestStationDatabase</c> in
/// Story345_PaWireProof.cs, <c>SensorGateStationDatabase</c> in Story366_SensorWorksWithAdminOff.cs)
/// is a thin <c>file</c>-scoped subclass whose own <c>StartAsync</c> calls <see cref="Provision"/>
/// with its own compose project-name prefix, then its own constructor — a <c>file</c> type cannot
/// itself be the shared base (it cannot cross files), so the shared machinery lives here instead and
/// each caller supplies only what genuinely varies between them.
/// </summary>
internal abstract class EphemeralStationDatabase : IAsyncDisposable
{
    readonly string project;
    readonly string composeFile;
    bool disposed;

    public string LibraryConnectionString { get; }
    public string StationConnectionString { get; }

    protected EphemeralStationDatabase(
        string project, string composeFile, string libraryConnectionString, string stationConnectionString)
    {
        this.project = project;
        this.composeFile = composeFile;
        LibraryConnectionString = libraryConnectionString;
        StationConnectionString = stationConnectionString;
    }

    /// <summary>Compose-up + host-port discovery — everything a subclass's own <c>StartAsync</c>
    /// needs before it can construct itself and call <see cref="WaitForSchemaAsync"/>.
    /// <paramref name="projectPrefix"/> is the one axis callers vary (so two concurrent test runs'
    /// ephemeral Postgres instances never collide on a compose project name); the compose file
    /// location, the wait, and the port-parsing are identical machinery either way. Synchronous, not
    /// <c>async</c>: every step here is a blocking <c>docker compose</c> process call
    /// (<see cref="RunCapture"/>'s own <c>Process.WaitForExit()</c>) — there is no genuine
    /// asynchronous work to await until <see cref="WaitForSchemaAsync"/>'s own connection retries.</summary>
    protected static (string Project, string ComposeFile, string Library, string Station) Provision(string projectPrefix)
    {
        var project = $"{projectPrefix}-{Guid.NewGuid():N}"[..24];
        var composeFile = LocateComposeFile();
        Compose(project, composeFile, "up", "-d", "--wait");

        var port = DiscoverHostPort(project, composeFile);
        var library = $"Host=localhost;Port={port};Database=genwave;Username=library_svc;Password=libtest;Search Path=library";
        var station = $"Host=localhost;Port={port};Database=genwave;Username=station_svc;Password=stationtest;Search Path=station";

        return (project, composeFile, library, station);
    }

    protected async Task WaitForSchemaAsync()
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

        throw new InvalidOperationException("station schema not ready on the ephemeral test database");
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        try { Compose(project, composeFile, "down", "-v"); } catch { /* best-effort teardown */ }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Pipes <paramref name="hostScriptPath"/> to <c>bash -s</c> inside this instance's own
    /// <c>testdb</c> compose service — a deliberate COPY of
    /// <c>GenWave.MediaLibrary.Tests.DatabaseFixture.RunFileInContainer</c>, body kept identical on
    /// purpose (T354 review LOW-3: no shared test-support project exists between the two test
    /// assemblies to hang a single source of truth off of, so this is duplication-by-necessity, not
    /// a drift risk to pretend away), exposed here so a subclass's own spec (Story367_
    /// TheStationRemembersEveryAiring.cs, PLAN T354) can run an in-place migration script — db/41's
    /// own one-shot seed step — against THIS instance's Postgres rather than inventing a second way
    /// to reach it.
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

    static void Compose(string project, string composeFile, params string[] verbAndArgs)
    {
        var args = new List<string> { "compose", "-p", project, "-f", composeFile };
        args.AddRange(verbAndArgs);
        Run("docker", args);
    }

    static int DiscoverHostPort(string project, string composeFile)
    {
        var output = RunCapture("docker", ["compose", "-p", project, "-f", composeFile, "port", "testdb", "5432"]).Trim();
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

    static string LocateComposeFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return Path.Combine(dir.FullName, "tests", "GenWave.MediaLibrary.Tests", "db-compose.yaml");
    }
}
