// STORY-438 — Script specs do not inherit the developer's shell (gh-#776 · PLAN T473–T475)
//
// Runner: xUnit. The subject is a test-support type that does not exist yet —
// GenWave.Host.Tests.Support.ScriptProcess, the ONE way a spec may start a bash script — so
// this file reaches it by reflection: the Host.Tests project keeps compiling, and every fact
// below fails with `pending: T473` until the helper lands. Once it exists the same facts pin
// its contract: a strip rule (IsStripped) that names the launch seams, a Run that applies it,
// and a repo-wide grep proving no spec bypasses it.
//
// The real-process facts seed the PARENT (this test process) with canaries under the stripped
// prefixes — GW_SPEC_CANARY_438, SKIP_SPEC_CANARY_438, COMPOSE_SPEC_CANARY_438 — never with
// SKIP_PREFLIGHT or a secret: setting those process-wide would race the other script specs
// running alongside (Host suite: xUnit.MaxParallelThreads=3). The exact-name rule is pinned
// through IsStripped instead.
//
// RED at plan time: the type is absent, and sixteen spec files construct ProcessStartInfo("bash")
// themselves.

using System.Reflection;
using GenWave.Host.Tests.Support;
using Xunit.Sdk;

namespace GenWave.Host.Tests.Specs;

public static class FeatureScriptSpecsDoNotInheritTheDevelopersShell
{
    const string HelperTypeName = "GenWave.Host.Tests.Support.ScriptProcess";
    const string Pending = "pending: T473 — tests/GenWave.Host.Tests/Support/ScriptProcess.cs does not exist yet";

    static readonly Lazy<Type> Helper = new(() =>
        typeof(FeatureScriptSpecsDoNotInheritTheDevelopersShell).Assembly.GetType(HelperTypeName)
            ?? throw new XunitException(Pending));

    static bool IsStripped(string name)
    {
        var method = Helper.Value.GetMethod("IsStripped", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
            ?? throw new XunitException($"{Pending} (no public static bool IsStripped(string))");
        return method.Invoke(null, [name]) is bool stripped
            ? stripped
            : throw new XunitException("IsStripped did not return a bool");
    }

    sealed record Run(int ExitCode, string StdOut, string StdErr)
    {
        /// <summary>The child's `NAME=value` lines, as printed by the probe script.</summary>
        public IReadOnlyDictionary<string, string> Vars => StdOut
            .Split('\n')
            .Select(l => l.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0])
            .ToDictionary(g => g.Key, g => g.First()[1]);
    }

    /// <summary>ScriptProcess.Run(script, binDir, envFile, extraEnv, args) — via reflection until T473.</summary>
    static Run RunThroughHelper(string script, string binDir, string? envFile, IReadOnlyDictionary<string, string>? extraEnv, params string[] args)
    {
        var method = Helper.Value.GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(string), typeof(string), typeof(IReadOnlyDictionary<string, string>), typeof(string[])])
            ?? throw new XunitException($"{Pending} (no public static Run(string, string, string?, IReadOnlyDictionary<string,string>?, params string[]))");

        var result = method.Invoke(null, [script, binDir, envFile, extraEnv, args]);
        if (result is not ValueTuple<int, string, string> tuple)
            throw new XunitException("Run did not return (int ExitCode, string StdOut, string StdErr)");
        return new Run(tuple.Item1, tuple.Item2, tuple.Item3);
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

    static void AddStub(string bin, string name, string body)
    {
        var path = Path.Combine(bin, name);
        File.WriteAllText(path, "#!/usr/bin/env bash\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    static string MakeBinDir(params string[] tools)
    {
        var dir = Directory.CreateTempSubdirectory("story438-bin-").FullName;
        foreach (var tool in tools)
            File.CreateSymbolicLink(Path.Combine(dir, tool), ResolveTool(tool));
        return dir;
    }

    /// <summary>A script that prints the child's view of the variables under test, one NAME=value per line.</summary>
    static string WriteProbeScript()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("story438-probe-").FullName, "probe.sh");
        File.WriteAllText(path, """
            #!/usr/bin/env bash
            for name in GW_SPEC_CANARY_438 SKIP_SPEC_CANARY_438 COMPOSE_SPEC_CANARY_438 \
                        SKIP_PREFLIGHT GW_ENV_FILE HOME LANG TMPDIR PATH; do
              if [ -n "${!name+set}" ]; then
                printf '%s=%s\n' "$name" "${!name}"
              else
                printf '%s=<unset>\n' "$name"
              fi
            done
            """);
        return path;
    }

    static string RepoRoot => RepoRootLocator.Find(AppContext.BaseDirectory);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the helper strips, keeps, and overrides exactly as ruled
    // ---------------------------------------------------------------------

    public static class ScenarioAParentShellsScriptVariablesDoNotReachTheChild
    {
        static readonly Lazy<(Run Run, string Bin)> Probe = new(() =>
        {
            // Parent-side canaries: one per stripped prefix. Harmless to every other spec.
            Environment.SetEnvironmentVariable("GW_SPEC_CANARY_438", "leaked");
            Environment.SetEnvironmentVariable("SKIP_SPEC_CANARY_438", "leaked");
            Environment.SetEnvironmentVariable("COMPOSE_SPEC_CANARY_438", "leaked");
            var bin = MakeBinDir("bash");
            return (RunThroughHelper(WriteProbeScript(), bin, envFile: null, extraEnv: null), bin);
        });

        [Fact]
        public static void The_probe_ran()
            => Assert.Equal(0, Probe.Value.Run.ExitCode);

        [Fact]
        public static void A_gw_prefixed_parent_variable_is_stripped()
            => Assert.Equal("<unset>", Probe.Value.Run.Vars.GetValueOrDefault("GW_SPEC_CANARY_438"));

        [Fact]
        public static void A_skip_prefixed_parent_variable_is_stripped()
            => Assert.Equal("<unset>", Probe.Value.Run.Vars.GetValueOrDefault("SKIP_SPEC_CANARY_438"));

        [Fact]
        public static void A_compose_prefixed_parent_variable_is_stripped()
            => Assert.Equal("<unset>", Probe.Value.Run.Vars.GetValueOrDefault("COMPOSE_SPEC_CANARY_438"));

        [Fact]
        public static void Home_survives()
            => Assert.Equal(Environment.GetEnvironmentVariable("HOME"), Probe.Value.Run.Vars.GetValueOrDefault("HOME"));

        [Fact]
        public static void The_path_is_exactly_the_scratch_bin_dir()
            => Assert.Equal(Probe.Value.Bin, Probe.Value.Run.Vars.GetValueOrDefault("PATH"));
    }

    public static class ScenarioTheStripRuleNamesTheLaunchSeams
    {
        [Fact]
        public static void Skip_preflight_is_stripped()
            => Assert.True(IsStripped("SKIP_PREFLIGHT"));

        [Fact]
        public static void Skip_tests_is_stripped()
            => Assert.True(IsStripped("SKIP_TESTS"));

        [Fact]
        public static void Compose_profiles_is_stripped()
            => Assert.True(IsStripped("COMPOSE_PROFILES"));

        [Fact]
        public static void Compose_file_is_stripped()
            => Assert.True(IsStripped("COMPOSE_FILE"));

        [Fact]
        public static void Gw_env_file_is_stripped()
            => Assert.True(IsStripped("GW_ENV_FILE"));

        [Fact]
        public static void Admin_password_is_stripped()
            => Assert.True(IsStripped("ADMIN_PASSWORD"));

        [Fact]
        public static void Media_dir_is_stripped()
            => Assert.True(IsStripped("MEDIA_DIR"));

        [Fact]
        public static void Postgres_password_is_stripped()
            => Assert.True(IsStripped("POSTGRES_PASSWORD"));

        [Fact]
        public static void Icecast_source_password_is_stripped()
            => Assert.True(IsStripped("ICECAST_SOURCE_PASSWORD"));

        [Fact]
        public static void Build_is_stripped()
            => Assert.True(IsStripped("BUILD"));

        [Fact]
        public static void Config_is_stripped()
            => Assert.True(IsStripped("CONFIG"));

        [Fact]
        public static void Home_is_kept()
            => Assert.False(IsStripped("HOME"));

        [Fact]
        public static void Lang_is_kept()
            => Assert.False(IsStripped("LANG"));

        [Fact]
        public static void Tmpdir_is_kept()
            => Assert.False(IsStripped("TMPDIR"));
    }

    public static class ScenarioAnExplicitOverrideStillFlowsThrough
    {
        static readonly string EnvFile = Path.Combine(Directory.CreateTempSubdirectory("story438-env-").FullName, "test.env");

        static readonly Lazy<Run> Probe = new(() =>
        {
            File.WriteAllText(EnvFile, "POSTGRES_PASSWORD=x\n");
            return RunThroughHelper(WriteProbeScript(), MakeBinDir("bash"), EnvFile,
                new Dictionary<string, string> { ["SKIP_PREFLIGHT"] = "1" });
        });

        [Fact]
        public static void A_spec_that_asks_for_skip_preflight_gets_it()
            => Assert.Equal("1", Probe.Value.Vars.GetValueOrDefault("SKIP_PREFLIGHT"));

        [Fact]
        public static void The_env_file_seam_is_applied_after_the_strip()
            => Assert.Equal(EnvFile, Probe.Value.Vars.GetValueOrDefault("GW_ENV_FILE"));
    }

    public static class ScenarioTheLaunchScriptUnderTheHelperRunsItsPreflight
    {
        // The deployed entry point: ./launch.sh through the helper, with no env file and no
        // override. Whatever the parent shell exported (a CI runner's SKIP_PREFLIGHT=1, the
        // developer's .env values), preflight must run and stop at the missing secrets.
        static readonly Lazy<Run> Launch = new(() =>
        {
            var bin = MakeBinDir("bash", "sh", "grep", "tail", "cut", "seq", "sleep", "awk", "dirname", "cat", "paste",
                "mktemp", "sed", "rm", "wc", "touch", "head", "sort", "date");
            AddStub(bin, "docker", """
                case "${1:-}" in info) exit 0 ;; esac
                case "$*" in *" version"*) echo "Docker Compose version v2.29.0" ;; esac
                exit 0
                """);
            var scratch = Directory.CreateTempSubdirectory("story438-repo-").FullName;
            foreach (var entry in Directory.EnumerateFileSystemEntries(RepoRoot))
            {
                var name = Path.GetFileName(entry);
                if (name is ".env" or ".git") continue;
                if (Directory.Exists(entry)) Directory.CreateSymbolicLink(Path.Combine(scratch, name), entry);
                else File.CreateSymbolicLink(Path.Combine(scratch, name), entry);
            }
            return RunThroughHelper(Path.Combine(scratch, "launch.sh"), bin, envFile: null, extraEnv: null);
        });

        [Fact]
        public static void The_launch_refuses()
            => Assert.NotEqual(0, Launch.Value.ExitCode);

        [Fact]
        public static void Preflight_ran_and_stopped_at_the_missing_env_file()
            => Assert.Contains("No .env file found", Launch.Value.StdErr);
    }

    public static class ScenarioEverySpecStartsScriptsThroughTheHelper
    {
        static readonly Lazy<string[]> FilesConstructingBash = new(() =>
            Directory.EnumerateFiles(Path.Combine(RepoRoot, "tests", "GenWave.Host.Tests", "Specs"), "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains("new ProcessStartInfo(\"bash\")", StringComparison.Ordinal))
                .Select(f => Path.GetRelativePath(RepoRoot, f))
                .Order()
                .ToArray());

        [Fact]
        public static void The_helper_lives_in_support()
            => Assert.True(File.Exists(Path.Combine(RepoRoot, "tests", "GenWave.Host.Tests", "Support", "ScriptProcess.cs")));

        [Fact]
        public static void No_spec_constructs_a_bash_process_itself()
            => Assert.Empty(FilesConstructingBash.Value);
    }
}
