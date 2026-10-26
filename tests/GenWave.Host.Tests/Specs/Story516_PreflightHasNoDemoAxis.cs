// STORY-516 — preflight has no demo axis (F216 · PLAN T644)
//
// BDD specification — xUnit. Absence facts over tools/preflight.sh's text (AC1/AC2) and one
// behavioural fact: with the old knob exported anyway, the script never probes 80/443 (AC3, read
// from the ports it names).

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePreflightHasNoDemoAxis
{
    static string Root => RepoRootLocator.Find(AppContext.BaseDirectory);

    static readonly Lazy<string> Script = new(() => File.ReadAllText(Path.Combine(Root, "tools", "preflight.sh")));

    public static class ScenarioTheDemoKnobIsGone
    {
        [Fact]
        public static void No_demo_flag()
        {
            Assert.DoesNotContain("GW_PREFLIGHT_DEMO", Script.Value, StringComparison.Ordinal);
        }

        [Fact]
        public static void No_demo_port_list()
        {
            Assert.DoesNotContain("GW_PREFLIGHT_DEMO_PORTS", Script.Value, StringComparison.Ordinal);
        }
    }

    public static class ScenarioTheWordingIsGone
    {
        [Fact]
        public static void No_posture_word()
        {
            Assert.DoesNotContain("appli" + "ance", Script.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class ScenarioThePortSetIsTheHomeSet
    {
        static readonly string RealPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

        // Sources the script and calls only the port check, like launch.sh does. A missing probe
        // tool makes the check WARN with every port it WOULD probe, which is what we read.
        static readonly Lazy<string> Output = new(() =>
        {
            var dir = TempDir.CreateForProcessLifetime();
            var envFile = Path.Combine(dir, ".env");
            HomeSetupHarness.WriteEnvFile(envFile, HomeSetupHarness.HealthyEnvValues("compose.yaml"));
            var scriptPath = Path.Combine(dir, "run.sh");
            File.WriteAllText(scriptPath, "set -euo pipefail; . tools/preflight.sh; preflight_ports");
            var run = ScriptProcess.Run(scriptPath, RealPath, envFile,
                new Dictionary<string, string>
                {
                    ["GW_SS_CMD"] = "gw-no-such-port-tool",
                    ["GW_PREFLIGHT_DEMO"] = "1",
                });
            return run.StdOut + run.StdErr;
        });

        [Fact]
        public static void The_base_ports_are_listed()
        {
            Assert.Contains("8000", Output.Value, StringComparison.Ordinal);
        }

        [Fact]
        public static void Port_80_is_never_probed()
        {
            Assert.DoesNotContain(" 80 ", Output.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(" 80.", Output.Value, StringComparison.Ordinal);
        }

        [Fact]
        public static void Port_443_is_never_probed()
        {
            Assert.DoesNotContain("443", Output.Value, StringComparison.Ordinal);
        }
    }
}
