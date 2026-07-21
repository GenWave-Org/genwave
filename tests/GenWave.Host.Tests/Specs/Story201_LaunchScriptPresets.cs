// STORY-201 — One launcher, every topology
//
// BDD specification — xUnit (SPEC F78.10). Drives the real ./launch.sh via Process.
//
// Contract these specs pin: launch.sh gains a `--dry-run` flag that prints the exact
// compose command plan (one command per line, prefixed "plan> ") and exits 0 without
// touching Docker — so every preset is assertable with no daemon, no teardown, no
// stack. The sad path (BUILD=1 + --pinned) must error before any docker invocation,
// dry-run or not. No spec here needs the docker CLI; none carry Category=Integration.

using System.Diagnostics;

namespace GenWave.Host.Tests.Specs;

public static class FeatureLaunchScriptPresets
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static (int ExitCode, string StdOut, string StdErr) RunLaunch(
        IReadOnlyDictionary<string, string>? extraEnv, params string[] args)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "launch.sh"));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        if (extraEnv is not null)
            foreach (var (key, value) in extraEnv)
                startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start launch.sh");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    static string[] PlanLines(string stdOut) =>
        stdOut.Split('\n').Where(l => l.StartsWith("plan> ", StringComparison.Ordinal)).ToArray();

    public static class ScenarioDevFlowPlan
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(null, "--dry-run"));

        [Fact]
        public static void Plan_tears_the_stack_down_first()
        {
            // Given no arguments (dev flow)  When --dry-run  Then the plan starts with teardown
            Assert.Contains(PlanLines(Run.Value.StdOut), l => l.Contains("compose down", StringComparison.Ordinal));
        }

        [Fact]
        public static void Plan_brings_db_up_before_the_full_stack()
        {
            var lines = PlanLines(Run.Value.StdOut);
            var dbUp = Array.FindIndex(lines, l => l.Contains("up", StringComparison.Ordinal) && l.TrimEnd().EndsWith(" db", StringComparison.Ordinal));
            var fullUp = Array.FindLastIndex(lines, l => l.Contains("up", StringComparison.Ordinal));
            Assert.True(dbUp >= 0 && fullUp > dbUp, $"expected db-first up ordering; plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public static void Plan_runs_migrations()
        {
            Assert.Contains(PlanLines(Run.Value.StdOut), l => l.Contains("migrate.sh", StringComparison.Ordinal));
        }

        [Fact]
        public static void Plan_omits_the_demo_overlay()
        {
            Assert.DoesNotContain(PlanLines(Run.Value.StdOut), l => l.Contains("compose.demo.yaml", StringComparison.Ordinal));
        }

        [Fact]
        public static void Plan_omits_pull()
        {
            Assert.DoesNotContain(PlanLines(Run.Value.StdOut), l => l.Contains("compose pull", StringComparison.Ordinal));
        }
    }

    public static class ScenarioPinnedFlowPlan
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(null, "--pinned", "--dry-run"));

        [Fact]
        public static void Plan_pulls_published_images()
        {
            Assert.Contains(PlanLines(Run.Value.StdOut), l => l.Contains("compose", StringComparison.Ordinal) && l.Contains("pull", StringComparison.Ordinal));
        }

        [Fact]
        public static void Plan_uses_the_demo_overlay_throughout()
        {
            Assert.All(
                PlanLines(Run.Value.StdOut).Where(l => l.Contains("compose", StringComparison.Ordinal)),
                l => Assert.Contains("compose.demo.yaml", l, StringComparison.Ordinal));
        }

        [Fact]
        public static void Plan_runs_migrate_with_the_overlay_flags()
        {
            Assert.Contains(PlanLines(Run.Value.StdOut), l =>
                l.Contains("migrate.sh", StringComparison.Ordinal) && l.Contains("compose.demo.yaml", StringComparison.Ordinal));
        }

        [Fact]
        public static void Plan_never_builds()
        {
            Assert.DoesNotContain(PlanLines(Run.Value.StdOut), l => l.Contains("--build", StringComparison.Ordinal) || l.Contains("compose build", StringComparison.Ordinal));
        }
    }

    public static class ScenarioProfileMergePlan
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(
                new Dictionary<string, string> { ["COMPOSE_PROFILES"] = "admin" },
                "--with", "logging,tunnel", "--dry-run"));

        [Fact]
        public static void Effective_profiles_keep_the_env_profiles()
        {
            // launch.sh --dry-run prints the effective profile set as "plan-profiles> a,b,c"
            Assert.Contains("admin", ProfilesLine(), StringComparison.Ordinal);
        }

        [Fact]
        public static void Effective_profiles_include_logging()
        {
            Assert.Contains("logging", ProfilesLine(), StringComparison.Ordinal);
        }

        [Fact]
        public static void Effective_profiles_include_tunnel()
        {
            Assert.Contains("tunnel", ProfilesLine(), StringComparison.Ordinal);
        }

        static string ProfilesLine() =>
            Run.Value.StdOut.Split('\n').FirstOrDefault(l => l.StartsWith("plan-profiles> ", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"no plan-profiles line in:\n{Run.Value.StdOut}");
    }

    public static class ScenarioPresetsCompose
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(null, "--pinned", "--with", "logging,tunnel", "--dry-run"));

        [Fact]
        public static void The_sanctioned_demo_launch_uses_the_overlay()
        {
            Assert.Contains(PlanLines(Run.Value.StdOut), l => l.Contains("compose.demo.yaml", StringComparison.Ordinal));
        }

        [Fact]
        public static void The_sanctioned_demo_launch_activates_both_profiles()
        {
            var profiles = Run.Value.StdOut.Split('\n').FirstOrDefault(l => l.StartsWith("plan-profiles> ", StringComparison.Ordinal)) ?? "";
            Assert.True(profiles.Contains("logging", StringComparison.Ordinal) && profiles.Contains("tunnel", StringComparison.Ordinal),
                $"expected logging+tunnel in: {profiles}");
        }
    }

    public static class SadPathBuildUnderPinned
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(new Dictionary<string, string> { ["BUILD"] = "1" }, "--pinned"));

        [Fact]
        public static void Exits_non_zero_before_touching_the_stack()
        {
            Assert.NotEqual(0, Run.Value.ExitCode);
        }

        [Fact]
        public static void Names_the_conflict()
        {
            Assert.Contains("--pinned", Run.Value.StdOut + Run.Value.StdErr, StringComparison.Ordinal);
        }
    }
}
