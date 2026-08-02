// gh-#332 — launch.sh's --pinned partial-up failure path printed `compose ps` (running-only),
// which structurally omits the container that just failed to start.
//
// BDD specification — xUnit. Drives the REAL ./launch.sh via Process with a scripted `docker`
// on PATH, the Gh019_ScriptPreflight.cs idiom extended one step further: where those scenarios
// all exit inside tools/preflight.sh before the first docker call, these deliberately run the
// whole --pinned flow (pull -> db up -> health poll -> migrate.sh -> up) against the stub, so
// the failure path under test is actually reached. No daemon, no images, no stack — every
// docker invocation is answered by a bash script in a scratch bin dir.
//
// The field failure this pins (Pi 5, v2.9.0): the daemon refused to start genwave-caddy-1 with
// a stale network id, and `compose ps` reported all seven OTHER containers Up/healthy under a
// "failed part-way" verdict. The operator was told to inspect a service the output never named.

using System.Diagnostics;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePinnedFailurePathShowsStoppedContainers
{
    /// <summary>Coreutils launch.sh, migrate.sh and tools/preflight.sh need between them.</summary>
    static readonly string[] BaseTools =
    [
        "bash", "sh", "grep", "tail", "cut", "seq", "sleep", "awk", "dirname", "cat", "paste",
        "mktemp", "sed", "rm", "wc", "touch",
    ];

    static readonly string[] RequiredEnvVars =
    [
        "POSTGRES_PASSWORD", "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD",
        "ICECAST_SOURCE_PASSWORD", "ICECAST_ADMIN_PASSWORD", "MEDIA_DIR",
    ];

    // The stub answers every docker call the --pinned flow makes, logs each one to
    // $GW_DOCKER_LOG, and fails the FULL `up -d` the way the daemon did on the Pi. Ordering in
    // the case matters: `up -d --no-recreate db` (launch.sh's db-first step) must be matched
    // before the bare `up -d` that this spec forces to fail.
    const string DockerStub = """
        printf '%s\n' "$*" >> "$GW_DOCKER_LOG"
        case "${1:-}" in
          info) exit 0 ;;
          inspect)
            case "$*" in
              *State.Health.Status*) echo healthy; exit 0 ;;
              *State.Running*)       echo true;    exit 0 ;;
            esac
            exit 0 ;;
        esac
        case "$*" in
          *" ps -q db"*)             echo deadbeefcafe; exit 0 ;;
          *" ps -a"*)                echo "NAME STATUS"; exit 0 ;;
          *" exec -T db "*)          exit 0 ;;
          *" up -d --no-recreate db"*) exit 0 ;;
          *" up -d"*)
            echo "Error response from daemon: failed to set up container networking: network 51b1bbeb not found" >&2
            exit 1 ;;
        esac
        exit 0
        """;

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static string ResolveTool(string tool)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var candidate = Path.Combine(dir, tool);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException($"required tool not on PATH: {tool}");
    }

    static string MakeBinDirWithDockerStub()
    {
        var dir = Directory.CreateTempSubdirectory("gh332-bin-").FullName;
        foreach (var tool in BaseTools)
            File.CreateSymbolicLink(Path.Combine(dir, tool), ResolveTool(tool));

        var stub = Path.Combine(dir, "docker");
        File.WriteAllText(stub, "#!/usr/bin/env bash\n" + DockerStub + "\n");
        // These specs only ever run on a Unix host (CI + dev are both Linux); the guard exists
        // to satisfy CA1416, not to support Windows.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(stub,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return dir;
    }

    /// <summary>The six required secrets, via preflight's GW_ENV_FILE seam — never the real .env.</summary>
    static string WriteEnvFile() =>
        WriteEnvFile([
            "POSTGRES_PASSWORD=x", "LIBRARY_DB_PASSWORD=x", "STATION_DB_PASSWORD=x",
            "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x",
            $"MEDIA_DIR={Path.GetTempPath()}",
        ]);

    static string WriteEnvFile(string[] assignments)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("gh332-env-").FullName, "test.env");
        File.WriteAllLines(path, assignments);
        return path;
    }

    sealed record Run(int ExitCode, string StdOut, string StdErr, string[] DockerCalls);

    static Run RunPinnedLaunch()
    {
        var bin = MakeBinDirWithDockerStub();
        var log = Path.Combine(Directory.CreateTempSubdirectory("gh332-log-").FullName, "docker.log");

        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "launch.sh"));
        startInfo.ArgumentList.Add("--pinned");

        startInfo.Environment["PATH"] = bin;
        startInfo.Environment["GW_ENV_FILE"] = WriteEnvFile();
        startInfo.Environment["GW_DOCKER_LOG"] = log;
        foreach (var name in RequiredEnvVars)
            startInfo.Environment.Remove(name);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start launch.sh");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var calls = File.Exists(log) ? File.ReadAllLines(log) : [];
        return new Run(process.ExitCode, stdOut, stdErr, calls);
    }

    static readonly Lazy<Run> PartialUp = new(RunPinnedLaunch);

    public static class ScenarioTheFullUpFailsUnderPinned
    {
        [Fact]
        public static void The_launch_stops_with_the_partial_up_verdict()
        {
            // Proves the flow actually reached the path under test — not an earlier preflight,
            // db or migration failure wearing the same non-zero exit.
            Assert.Equal(3, PartialUp.Value.ExitCode);
            Assert.Contains("failed part-way", PartialUp.Value.StdErr);
        }

        [Fact]
        public static void The_status_dump_asks_for_all_containers_not_just_running_ones()
        {
            // gh-#332 itself. `compose ps` without -a lists RUNNING containers only, so the one
            // service that failed to start — the only one worth printing here — was excluded by
            // construction. Nothing else in this flow may issue a bare `ps` either.
            var statusDumps = PartialUp.Value.DockerCalls
                .Where(c => c.Contains(" ps", StringComparison.Ordinal))
                .Where(c => !c.Contains(" ps -q db", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(statusDumps);
            Assert.All(statusDumps, c => Assert.Contains(" ps -a", c, StringComparison.Ordinal));
        }

        [Fact]
        public static void The_guidance_names_the_state_to_look_for()
        {
            // "status above" alone sent the operator hunting through seven green lines.
            Assert.Contains("NOT in an Up state", PartialUp.Value.StdErr);
        }

        [Fact]
        public static void The_guidance_offers_a_route_for_a_container_that_never_started()
        {
            // `logs <service>` is a dead end for this failure class: a container the daemon
            // refused to start produced no logs at all. The daemon's own reason lives in
            // .State.Error.
            Assert.Contains("docker inspect", PartialUp.Value.StdErr);
            Assert.Contains(".State.Error", PartialUp.Value.StdErr);
        }

        [Fact]
        public static void The_partial_stack_is_never_torn_down()
        {
            // never-silent outranks tidiness (launch.sh's --pinned contract, gh-#19): whatever is
            // still broadcasting keeps broadcasting. A `down` here would take the station off air
            // to tidy up a failed caddy.
            Assert.DoesNotContain(PartialUp.Value.DockerCalls,
                c => c.Contains(" down", StringComparison.Ordinal));
        }
    }
}
