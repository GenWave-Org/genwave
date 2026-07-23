// gh-#19 — Build/launch scripts need enhancements: preflight, guidance, never-half-a-stack.
//
// BDD specification — xUnit. Drives the REAL ./launch.sh and ./build.sh via Process, the exact
// Story201_LaunchScriptPresets.cs idiom: no Docker daemon, no teardown, no stack. Every scenario
// here exercises a FAILURE path that exits in tools/preflight.sh — strictly before the first
// docker/dotnet/compose invocation — so running them is always safe on any machine.
//
// Isolation: each scenario runs the script with PATH pointed at a scratch bin directory holding
// symlinks to the coreutils the scripts need plus (per scenario) a scripted `docker`/`dotnet`
// stub — "docker not installed" is simply a PATH without one. The six required .env secrets are
// scrubbed from the child environment and GW_ENV_FILE (a preflight-only test seam) points at a
// scratch env file, so the developer's real .env and shell exports can never sway an assertion.

using System.Diagnostics;

namespace GenWave.Host.Tests.Specs;

public static class FeatureScriptPreflight
{
    static readonly string[] RequiredEnvVars =
    [
        "POSTGRES_PASSWORD", "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD",
        "ICECAST_SOURCE_PASSWORD", "ICECAST_ADMIN_PASSWORD", "MEDIA_DIR",
    ];

    /// <summary>Coreutils the scripts themselves need — everything else is deliberately absent.</summary>
    static readonly string[] BaseTools =
        ["bash", "sh", "grep", "tail", "cut", "seq", "sleep", "awk", "dirname", "cat", "paste"];

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

    /// <summary>A scratch bin dir with the coreutils symlinked in and nothing else.</summary>
    static string MakeBinDir()
    {
        var dir = Directory.CreateTempSubdirectory("gw-preflight-bin-").FullName;
        foreach (var tool in BaseTools)
            File.CreateSymbolicLink(Path.Combine(dir, tool), ResolveTool(tool));
        return dir;
    }

    static void AddStub(string binDir, string name, string body)
    {
        var path = Path.Combine(binDir, name);
        File.WriteAllText(path, "#!/usr/bin/env bash\n" + body + "\n");
        // The scripts under test are bash — these specs only ever run on a Unix host (CI + dev
        // are both Linux); the guard exists to satisfy CA1416, not to support Windows.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>A docker stub whose `docker info` succeeds (daemon "running"), everything else a no-op.</summary>
    static string HealthyDockerStub => "exit 0";

    static string WriteEnvFile(params string[] assignments)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("gw-preflight-env-").FullName, "test.env");
        File.WriteAllLines(path, assignments);
        return path;
    }

    /// <summary>Every required secret set to a real-looking value; MEDIA_DIR to an existing dir.</summary>
    static string[] CompleteEnvAssignments(string? mediaDir = null) =>
    [
        "POSTGRES_PASSWORD=x", "LIBRARY_DB_PASSWORD=x", "STATION_DB_PASSWORD=x",
        "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x",
        $"MEDIA_DIR={mediaDir ?? Path.GetTempPath()}",
    ];

    static (int ExitCode, string StdOut, string StdErr) RunScript(
        string script, string binDir, string? envFile = null,
        IReadOnlyDictionary<string, string>? extraEnv = null, params string[] args)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), script));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        startInfo.Environment["PATH"] = binDir;
        foreach (var name in RequiredEnvVars)
            startInfo.Environment.Remove(name);
        if (envFile is not null)
            startInfo.Environment["GW_ENV_FILE"] = envFile;
        if (extraEnv is not null)
            foreach (var (key, value) in extraEnv)
                startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start {script}");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    // ---------------------------------------------------------------------
    // Docker preflight (launch.sh)
    // ---------------------------------------------------------------------

    public static class ScenarioDockerPreflight
    {
        [Fact]
        public static void DryRunStillNeedsNoDockerAndExitsZero()
        {
            // --dry-run's "touches nothing" contract (STORY-201) survives the gh-#19 preflight:
            // the plan prints on a machine with no docker at all.
            var (exitCode, stdOut, _) = RunScript("launch.sh", MakeBinDir(), args: "--dry-run");

            Assert.Equal(0, exitCode);
            Assert.Contains("plan> ", stdOut);
        }

        [Fact]
        public static void MissingDockerFailsWithInstallGuidance()
        {
            var (exitCode, _, stdErr) = RunScript("launch.sh", MakeBinDir());

            Assert.Equal(3, exitCode);
            Assert.Contains("Docker is not installed", stdErr);
            Assert.Contains("How to proceed", stdErr);
            Assert.Contains("docs.docker.com", stdErr);
        }

        [Fact]
        public static void DeadDaemonFailsWithStartGuidance()
        {
            var bin = MakeBinDir();
            AddStub(bin, "docker",
                """if [ "${1:-}" = "info" ]; then echo "Cannot connect to the Docker daemon" >&2; exit 1; fi; exit 0""");

            var (exitCode, _, stdErr) = RunScript("launch.sh", bin);

            Assert.Equal(3, exitCode);
            Assert.Contains("daemon is not running", stdErr);
            Assert.Contains("systemctl start docker", stdErr);
        }

        [Fact]
        public static void PermissionDeniedFailsWithDockerGroupGuidance()
        {
            var bin = MakeBinDir();
            AddStub(bin, "docker",
                """if [ "${1:-}" = "info" ]; then echo "permission denied while trying to connect to the Docker daemon socket" >&2; exit 1; fi; exit 0""");

            var (exitCode, _, stdErr) = RunScript("launch.sh", bin);

            Assert.Equal(3, exitCode);
            Assert.Contains("permission denied", stdErr);
            Assert.Contains("usermod -aG docker", stdErr);
        }

        [Fact]
        public static void SkipPreflightBypassesEveryCheck()
        {
            // The documented escape hatch: with SKIP_PREFLIGHT=1 the script gets PAST preflight
            // (and then fails at the first real docker call on this deliberately docker-less
            // PATH — with a non-preflight error).
            var (exitCode, _, stdErr) = RunScript(
                "launch.sh", MakeBinDir(), extraEnv: new Dictionary<string, string> { ["SKIP_PREFLIGHT"] = "1" });

            Assert.NotEqual(0, exitCode);
            Assert.DoesNotContain("preflight:", stdErr);
        }
    }

    // ---------------------------------------------------------------------
    // .env preflight (launch.sh; docker stubbed healthy so env is the first failing check)
    // ---------------------------------------------------------------------

    public static class ScenarioEnvSecretsPreflight
    {
        static string HealthyDockerBin()
        {
            var bin = MakeBinDir();
            AddStub(bin, "docker", HealthyDockerStub);
            return bin;
        }

        [Fact]
        public static void AMissingEnvFileFailsWithTheTemplateStep()
        {
            var (exitCode, _, stdErr) = RunScript(
                "launch.sh", HealthyDockerBin(), envFile: "/nonexistent/gw-test.env");

            Assert.Equal(3, exitCode);
            Assert.Contains("secrets are not configured", stdErr);
            Assert.Contains("cp .env.example .env", stdErr);
        }

        [Fact]
        public static void APlaceholderSecretFailsNamingTheVariable()
        {
            var envFile = WriteEnvFile(
                "POSTGRES_PASSWORD=change-me-postgres", "LIBRARY_DB_PASSWORD=x", "STATION_DB_PASSWORD=x",
                "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x", $"MEDIA_DIR={Path.GetTempPath()}");

            var (exitCode, _, stdErr) = RunScript("launch.sh", HealthyDockerBin(), envFile);

            Assert.Equal(3, exitCode);
            Assert.Contains("change-me placeholder", stdErr);
            Assert.Contains("POSTGRES_PASSWORD", stdErr);
        }

        [Fact]
        public static void AMissingSecretFailsNamingTheVariable()
        {
            var envFile = WriteEnvFile(
                "POSTGRES_PASSWORD=x", "STATION_DB_PASSWORD=x",
                "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x", $"MEDIA_DIR={Path.GetTempPath()}");

            var (exitCode, _, stdErr) = RunScript("launch.sh", HealthyDockerBin(), envFile);

            Assert.Equal(3, exitCode);
            Assert.Contains("missing", stdErr);
            Assert.Contains("LIBRARY_DB_PASSWORD", stdErr);
        }

        [Fact]
        public static void ANonexistentMediaDirFails()
        {
            var envFile = WriteEnvFile(CompleteEnvAssignments(mediaDir: "/no/such/gw-media-dir"));

            var (exitCode, _, stdErr) = RunScript("launch.sh", HealthyDockerBin(), envFile);

            Assert.Equal(3, exitCode);
            Assert.Contains("MEDIA_DIR", stdErr);
            Assert.Contains("/no/such/gw-media-dir", stdErr);
        }
    }

    // ---------------------------------------------------------------------
    // build.sh preflight
    // ---------------------------------------------------------------------

    public static class ScenarioBuildScriptPreflight
    {
        [Fact]
        public static void MissingDotnetSdkFailsWithInstallGuidance()
        {
            var (exitCode, _, stdErr) = RunScript("build.sh", MakeBinDir());

            Assert.Equal(3, exitCode);
            Assert.Contains(".NET SDK is not installed", stdErr);
            Assert.Contains("dotnet.microsoft.com", stdErr);
        }

        [Fact]
        public static void AWrongSdkMajorFailsNamingWhatWasFound()
        {
            var bin = MakeBinDir();
            AddStub(bin, "dotnet",
                """if [ "${1:-}" = "--list-sdks" ]; then echo "8.0.100 [/usr/lib/dotnet/sdk]"; exit 0; fi; exit 0""");

            var (exitCode, _, stdErr) = RunScript("build.sh", bin);

            Assert.Equal(3, exitCode);
            Assert.Contains(".NET SDK 10.x is required", stdErr);
            Assert.Contains("8.0.100", stdErr);
        }

        [Fact]
        public static void WithASuitableSdkTheDockerCheckIsNext()
        {
            // dotnet passes, docker is absent — proving check ORDER (tooling before daemon
            // before secrets) and that build.sh gates on docker too.
            var bin = MakeBinDir();
            AddStub(bin, "dotnet",
                """if [ "${1:-}" = "--list-sdks" ]; then echo "10.0.100 [/usr/lib/dotnet/sdk]"; exit 0; fi; exit 0""");

            var (exitCode, _, stdErr) = RunScript("build.sh", bin);

            Assert.Equal(3, exitCode);
            Assert.Contains("Docker is not installed", stdErr);
        }
    }
}
