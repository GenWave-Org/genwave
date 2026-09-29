// gh-#791 + gh-#771 — the safe branch outlives the api, and a stalled api can't wedge it.
//
// engine/genwave.liq's safe resolver remembers the last good /internal/safe-track answers and
// replays them round-robin while the api is unreachable, stalled, or erroring; a 204 forgets
// them (mksafe, F4.4). curl is bounded (--connect-timeout 2 --max-time 5). The block between
// `# >>> gw-safe-resolver` and `# <<< gw-safe-resolver` is sliced out of the real script and run
// in real Liquidsoap — fake commands for the cache rules, the real curl line against a stub api
// on a private docker network for the stall.

using GenWave.Host.Tests.Support;
using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafeResolverSurvivesApiOutage
{
    static string RepoRoot => RepoRootLocator.Find(AppContext.BaseDirectory);

    static string EngineScriptText => File.ReadAllText(Path.Combine(RepoRoot, "engine", "genwave.liq"));

    const string BlockStart = "# >>> gw-safe-resolver";
    const string BlockEnd = "# <<< gw-safe-resolver";

    /// <summary>The resolver block exactly as genwave.liq ships it.</summary>
    static string ResolverBlock()
    {
        var script = EngineScriptText;
        var start = script.IndexOf(BlockStart, StringComparison.Ordinal);
        var end = script.IndexOf(BlockEnd, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "gw-safe-resolver markers not found in genwave.liq");
        return script[start..end];
    }

    /// <summary>The shipped `gw_safe_track_cmd = ref("curl …")` line.</summary>
    static string CommandLine() =>
        EngineScriptText.Split('\n').Single(l => l.StartsWith("gw_safe_track_cmd = ref(", StringComparison.Ordinal));

    /// <summary>
    /// Wraps the sliced block in a harness: <paramref name="prelude"/> defines gw_safe_track_cmd,
    /// <paramref name="steps"/> call step(label, …) which prints `RESULT label [uri]`. A 90 s
    /// watchdog shuts the harness down if a step hangs.
    /// </summary>
    static string Harness(string prelude, string stepDef, string steps) => $$"""
        settings.log.stdout := true
        settings.log.file := false
        settings.log.level := 3
        {{prelude}}
        {{ResolverBlock()}}
        {{stepDef}}
        def run() =
        {{steps}}
          shutdown()
        end
        def watchdog() =
          print("RESULT watchdog [fired]")
          shutdown()
        end
        thread.run(delay=0.5, run)
        thread.run(delay=90., watchdog)
        output.dummy(blank())
        """;

    /// <summary>Runs a harness in <paramref name="image"/>; returns (label → uri, full output).</summary>
    static (IReadOnlyDictionary<string, string> Results, string Output) RunHarness(
        string image, string harness, params string[] dockerArgs)
    {
        using var dir = new TempDir();
        // The temp dir is 0700; the engine image runs as the non-root liquidsoap user.
        var file = Path.Combine(dir.Path, "harness.liq");
        File.WriteAllText(file, harness);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dir.Path, (UnixFileMode)0b111_101_101);
            File.SetUnixFileMode(file, (UnixFileMode)0b110_100_100);
        }
        var args = new List<string> { "run", "--rm", "--pull", "never", "-v", $"{dir.Path}:/h:ro" };
        args.AddRange(dockerArgs);
        args.AddRange(["--entrypoint", "liquidsoap", image, "/h/harness.liq"]);
        var r = DockerCli.Run([.. args]);
        var output = r.Stdout + r.Stderr;
        Assert.True(r.ExitCode == 0, $"harness failed (exit {r.ExitCode}):\n{output}");

        var results = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(output, @"^RESULT (\S+) \[(.*)\]", RegexOptions.Multiline))
        {
            results[m.Groups[1].Value] = m.Groups[2].Value;
        }

        Assert.False(results.ContainsKey("watchdog"), $"harness watchdog fired:\n{output}");
        return (results, output);
    }

    // ---------------------------------------------------------------------
    // Script shape
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSafeBranchUsesTheBoundedResolver
    {
        [Fact]
        public void SafeLibResolvesThroughTheRememberingResolver()
        {
            var safeLine = EngineScriptText.Split('\n')
                .Single(l => l.Contains("request.dynamic", StringComparison.Ordinal) && l.Contains("safe_lib", StringComparison.Ordinal));
            Assert.Contains("request.create(gw_safe_resolve())", safeLine);
        }

        [Fact]
        public void TheFetchIsBoundedAndFailsOnHttpErrors()
        {
            var cmd = CommandLine();
            Assert.Contains("curl -sf ", cmd);
            Assert.Contains("--connect-timeout 2", cmd);
            Assert.Contains("--max-time 5", cmd);
            Assert.Contains("http://api:8080/internal/safe-track", cmd);
        }

        [Fact]
        public void TheMemoryIsWarmedInTheBackground()
        {
            Assert.Contains("thread.run.recurrent(delay=10., gw_safe_warm)", EngineScriptText);
        }
    }

    // ---------------------------------------------------------------------
    // Cache rules — the real block in real Liquidsoap, fake fetch commands
    // ---------------------------------------------------------------------

    public sealed class RulesRun
    {
        public const string Image = "savonet/liquidsoap:v2.4.5";

        public IReadOnlyDictionary<string, string> Results { get; }
        public string Output { get; }

        public RulesRun()
        {
            DockerCli.Arrange("pull", "-q", Image);

            // 20 distinct answers overflow the 16-deep memory; 16 outage replays then walk it once.
            var overflow = string.Concat(Enumerable.Range(1, 20)
                .Select(i => $"  step(\"fill-{i}\", \"echo annotate:gw:\\\"t{i}\\\":/t{i}.mp3\")\n"));
            var walk = string.Concat(Enumerable.Range(1, 16).Select(i => $"  step(\"walk-{i}\", \"exit 7\")\n"));

            var steps = """
                  step("cold-outage", "exit 7")
                  step("good-a", "echo annotate:gw:\"a\":/a.mp3")
                  step("good-b", "echo annotate:gw:\"b\":/b.mp3")
                  step("good-a-again", "echo annotate:gw:\"a\":/a.mp3")
                  step("unreachable-1", "exit 7")
                  step("unreachable-2", "exit 7")
                  step("dns", "exit 6")
                  step("timeout", "exit 28")
                  step("http-error", "exit 22")
                  step("empty", "true")
                  step("after-empty-outage", "exit 7")

                """ + overflow + walk;

            (Results, Output) = RunHarness(
                Image,
                Harness(
                    "gw_safe_track_cmd = ref(\"\")",
                    """
                    def step(label, cmd) =
                      gw_safe_track_cmd := cmd
                      print("RESULT #{label} [#{gw_safe_resolve()}]")
                    end
                    """,
                    steps));
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheResolverRemembersAndReplays(RulesRun run) : IClassFixture<RulesRun>
    {
        [Fact]
        public void AGoodAnswerIsPassedThrough()
        {
            Assert.Equal("annotate:gw:a:/a.mp3", run.Results["good-a"]);
            Assert.Equal("annotate:gw:b:/b.mp3", run.Results["good-b"]);
        }

        [Fact]
        public void AnOutageReplaysRememberedTracksInTurn()
        {
            // Memory is newest-first and distinct: [b, a].
            Assert.Equal("annotate:gw:b:/b.mp3", run.Results["unreachable-1"]);
            Assert.Equal("annotate:gw:a:/a.mp3", run.Results["unreachable-2"]);
            Assert.Equal("annotate:gw:b:/b.mp3", run.Results["dns"]);
            Assert.Equal("annotate:gw:a:/a.mp3", run.Results["timeout"]);
            Assert.Equal("annotate:gw:b:/b.mp3", run.Results["http-error"]);
        }

        [Fact]
        public void AnOutageBeforeAnyAnswerFallsToSilence()
        {
            Assert.Equal("", run.Results["cold-outage"]);
            Assert.Contains("safe-track unreachable (curl 7); nothing remembered yet, falling to silence", run.Output);
        }

        [Fact]
        public void EachFailureKindLogsItsOwnLine()
        {
            Assert.Contains("safe-track unreachable (curl 6); replaying remembered safe track", run.Output);
            Assert.Contains("safe-track timed out (curl 28); replaying remembered safe track", run.Output);
            Assert.Contains("safe-track answered an HTTP error (curl 22); replaying remembered safe track", run.Output);
        }

        [Fact]
        public void AnEmptyAnswerForgetsEverything()
        {
            // 204 = the operator emptied the safe scope: silence, never a stale track (F4.4).
            Assert.Equal("", run.Results["empty"]);
            Assert.Equal("", run.Results["after-empty-outage"]);
            Assert.Contains("safe-track empty (204); safe scope has nothing eligible, forgetting remembered tracks", run.Output);
        }

        [Fact]
        public void TheMemoryHoldsTheSixteenNewest()
        {
            var walked = Enumerable.Range(1, 16).Select(i => run.Results[$"walk-{i}"]).ToList();
            var expected = Enumerable.Range(5, 16).Select(i => $"annotate:gw:t{i}:/t{i}.mp3");
            Assert.Equal(expected.Order(), walked.Order());
            Assert.Contains("replaying remembered safe track 16 of 16", run.Output);
        }
    }

    // ---------------------------------------------------------------------
    // Wire — the shipped curl line in the engine image against a stub api
    // ---------------------------------------------------------------------

    public sealed class WireRun : IDisposable
    {
        const string Stub = "busybox:1.36.1-uclibc";

        /// <summary>What the stub api answers — the annotate shape /internal/safe-track returns.</summary>
        public const string Answer = "annotate:track_id=\"a\",replay_gain=\"-3.2 dB\":/media/a.mp3";
        readonly string network = $"gh791-{Guid.NewGuid():N}";

        public IReadOnlyDictionary<string, string> Results { get; }
        public string Output { get; }
        public TimeSpan StallElapsed { get; }

        public WireRun()
        {
            const string image = "gw-engine-spec:gh791";
            DockerCli.Arrange("pull", "-q", Stub);
            DockerCli.Arrange("build", "-q", "-t", image, Path.Combine(RepoRoot, "engine"));
            DockerCli.Arrange("network", "create", network);

            // `api` answers one annotate line; `stall` accepts the connection and never replies.
            DockerCli.Arrange("run", "-d", "--rm", "--name", $"{network}-api", "--network", network, "--network-alias", "api", Stub,
                "sh", "-c", "mkdir -p /www/internal && echo '" + Answer + "' > /www/internal/safe-track && httpd -f -p 8080 -h /www");
            DockerCli.Arrange("run", "-d", "--rm", "--name", $"{network}-stall", "--network", network, "--network-alias", "stall", Stub,
                "sh", "-c", "sleep 600 | nc -l -p 8080");

            var steps = """
                  step("answering", "api:8080")
                  step("stalled", "stall:8080")
                  step("gone", "nohost:8080")

                """;
            (Results, Output) = RunHarness(
                image,
                Harness(
                    CommandLine() + "\ngw_real_cmd = gw_safe_track_cmd()",
                    """
                    def step(label, host) =
                      gw_safe_track_cmd := string.replace(pattern="api:8080", fun (_) -> host, gw_real_cmd)
                      t0 = time()
                      uri = gw_safe_resolve()
                      print("RESULT #{label} [#{uri}]")
                      print("RESULT #{label}-ms [#{int_of_float((time() - t0) * 1000.)}]")
                    end
                    """,
                    steps),
                "--network", network);
            StallElapsed = TimeSpan.FromMilliseconds(int.Parse(Results["stalled-ms"]));
        }

        public void Dispose()
        {
            DockerCli.Run("rm", "-f", $"{network}-api", $"{network}-stall");
            DockerCli.Run("network", "rm", network);
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioAStalledApiCannotWedgeTheSafeBranch(WireRun run) : IClassFixture<WireRun>
    {
        [Fact]
        public void TheAnsweringApiIsRemembered()
        {
            Assert.Equal(WireRun.Answer, run.Results["answering"]);
        }

        [Fact]
        public void AStallIsCutOffWithinTheBoundAndReplays()
        {
            Assert.Equal(WireRun.Answer, run.Results["stalled"]);
            Assert.Contains("safe-track timed out (curl 28); replaying remembered safe track 1 of 1", run.Output);
            Assert.InRange(run.StallElapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8));
        }

        [Fact]
        public void AMissingApiReplays()
        {
            Assert.Equal(WireRun.Answer, run.Results["gone"]);
            Assert.Contains("safe-track unreachable (curl 6); replaying remembered safe track 1 of 1", run.Output);
        }
    }
}
