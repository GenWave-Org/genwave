// STORY-516 — preflight has no demo axis (F216 · PLAN T644)
//
// BDD specification — xUnit. Absence facts over tools/preflight.sh's text (AC1/AC2) and one
// behavioural fact: with the old knob exported anyway, the script never probes 80/443 (AC3, read
// from the ports it names). Every fact is pending until T644.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePreflightHasNoDemoAxis
{
    static string Root => RepoRootLocator.Find(AppContext.BaseDirectory);

    static readonly Lazy<string> Script = new(() => File.ReadAllText(Path.Combine(Root, "tools", "preflight.sh")));

    public static class ScenarioTheDemoKnobIsGone
    {
        [Fact(Skip = "pending: T644")]
        public static void No_demo_flag()
        {
            Assert.DoesNotContain("GW_PREFLIGHT_DEMO", Script.Value, StringComparison.Ordinal);
        }

        [Fact(Skip = "pending: T644")]
        public static void No_demo_port_list()
        {
            Assert.DoesNotContain("GW_PREFLIGHT_DEMO_PORTS", Script.Value, StringComparison.Ordinal);
        }
    }

    public static class ScenarioTheWordingIsGone
    {
        [Fact(Skip = "pending: T644")]
        public static void No_posture_word()
        {
            Assert.DoesNotContain("appli" + "ance", Script.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class ScenarioThePortSetIsTheHomeSet
    {
        static readonly string RealPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

        static readonly Lazy<string> Output = new(() =>
        {
            var envFile = Path.Combine(TempDir.CreateForProcessLifetime(), ".env");
            HomeSetupHarness.WriteEnvFile(envFile, HomeSetupHarness.HealthyEnvValues("compose.yaml"));
            var run = ScriptProcess.Run("tools/preflight.sh", RealPath, envFile,
                new Dictionary<string, string> { ["GW_PREFLIGHT_DEMO"] = "1", ["GW_PREFLIGHT_TOPOLOGY"] = "kokoro" });
            return run.StdOut + run.StdErr;
        });

        [Fact(Skip = "pending: T644")]
        public static void Port_80_is_never_probed()
        {
            Assert.DoesNotContain(" 80 ", Output.Value, StringComparison.Ordinal);
        }

        [Fact(Skip = "pending: T644")]
        public static void Port_443_is_never_probed()
        {
            Assert.DoesNotContain("443", Output.Value, StringComparison.Ordinal);
        }
    }
}
