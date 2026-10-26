// STORY-519 — the publish guard pins the LAN allowlist (F216 · PLAN T648)
//
// BDD specification — xUnit. AC1/AC3 run the real guards over the real compose files (docker
// CLI, Category=Integration like Story181). AC2 drives the publish guard's --config-file mode
// against a pre-rendered fixture (no docker). AC4 reads the workflow. Every fact is pending
// until T648.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePublishGuardPinsTheLanAllowlist
{
    static string Root => RepoRootLocator.Find(AppContext.BaseDirectory);

    static readonly string RealPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

    static (int ExitCode, string StdOut, string StdErr) RunGuard(string script, params string[] args) =>
        ScriptProcess.Run(script, RealPath, args: args);

    [Trait("Category", "Integration")]
    public static class ScenarioTheRealRendersPass
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunGuard("tools/check-compose-publish.sh"));

        [Fact(Skip = "pending: T648")]
        public static void Exits_zero()
        {
            Assert.True(Run.Value.ExitCode == 0, $"exit {Run.Value.ExitCode}\n{Run.Value.StdOut}\n{Run.Value.StdErr}");
        }

        [Theory(Skip = "pending: T648")]
        [InlineData("icecast:8000")]
        [InlineData("api:8080")]
        [InlineData("api:8081")]
        [InlineData("admin_ui:3000")]
        public static void Names_each_allowed_publish(string publish)
        {
            Assert.Contains(publish, Run.Value.StdOut, StringComparison.Ordinal);
        }
    }

    public static class SadPathAnythingElseIsRed
    {
        const string FixtureJson = """
            {
              "services": {
                "icecast": {
                  "ports": [
                    {"mode": "ingress", "target": 8000, "published": "8000", "protocol": "tcp"}
                  ]
                },
                "db": {
                  "ports": [
                    {"mode": "ingress", "target": 5432, "published": "5432", "protocol": "tcp"}
                  ]
                }
              }
            }
            """;

        static readonly Lazy<(int ExitCode, string Output)> Run = new(() =>
        {
            var fixture = Path.Combine(TempDir.CreateForProcessLifetime(), "render.json");
            File.WriteAllText(fixture, FixtureJson);
            var (exit, stdOut, stdErr) = RunGuard("tools/check-compose-publish.sh", "--config-file", fixture);
            return (exit, stdOut + stdErr);
        });

        [Fact(Skip = "pending: T648")]
        public static void Exits_non_zero()
        {
            Assert.NotEqual(0, Run.Value.ExitCode);
        }

        [Fact(Skip = "pending: T648")]
        public static void Names_the_service_and_port()
        {
            Assert.True(Run.Value.Output.Contains("db", StringComparison.Ordinal) && Run.Value.Output.Contains("5432", StringComparison.Ordinal), Run.Value.Output);
        }
    }

    [Trait("Category", "Integration")]
    public static class ScenarioTheSocketGuardPassesTheNewMatrix
    {
        [Fact(Skip = "pending: T648")]
        public static void Exits_zero()
        {
            var (exit, stdOut, stdErr) = RunGuard("tools/check-compose-socket.sh");

            Assert.True(exit == 0, $"exit {exit}\n{stdOut}\n{stdErr}");
        }
    }

    public static class ScenarioCiWatchesTheRightFiles
    {
        static readonly Lazy<string> Workflow = new(() =>
            File.ReadAllText(Path.Combine(Root, ".github", "workflows", "compose-publish-guard.yml")));

        [Fact(Skip = "pending: T648")]
        public static void Lists_the_piper_only_overlay()
        {
            Assert.Contains("compose.piper-only.yaml", Workflow.Value, StringComparison.Ordinal);
        }

        [Fact(Skip = "pending: T648")]
        public static void Does_not_list_the_demo_overlay()
        {
            Assert.DoesNotContain("compose." + "demo.yaml", Workflow.Value, StringComparison.Ordinal);
        }

        [Fact(Skip = "pending: T648")]
        public static void Does_not_list_the_proxy_config()
        {
            Assert.DoesNotContain("Cad" + "dyfile", Workflow.Value, StringComparison.Ordinal);
        }
    }
}
