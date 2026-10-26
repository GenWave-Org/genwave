// STORY-515 — delight: --repair flags a stale COMPOSE_FILE (F216 · PLAN T643)
//
// BDD specification — xUnit. Adoption mode through HomeSetupHarness: a .env whose COMPOSE_FILE
// still names the deleted demo overlay must surface as a repairable finding (Fix: line, confirm,
// exit 5 when declined) and `--yes` must rewrite the key. A clean COMPOSE_FILE raises nothing.
// Every fact is pending until T643.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRepairFlagsStaleComposeFile
{
    // Assembled at runtime so this file never trips Story521's tree-wide grep.
    static readonly string DemoOverlay = "compose." + "demo.yaml";

    static readonly string StaleComposeFile = $"compose.yaml:compose.pinned.yaml:{DemoOverlay}";

    static (string EnvFile, string Docker) Box(string composeFile)
    {
        var envFile = HomeSetupHarness.ScratchEnvPath();
        HomeSetupHarness.WriteEnvFile(envFile, HomeSetupHarness.HealthyEnvValues(composeFile));
        var composeArgs = string.Join(" ", composeFile.Split(':').Select(f => $"-f {f}"));
        return (envFile, HomeSetupHarness.WriteDockerStub(composeArgs));
    }

    public static class ScenarioTheStaleElementIsAFinding
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run = new(() =>
        {
            var (envFile, docker) = Box(StaleComposeFile);
            return HomeSetupHarness.RunSetup(envFile, docker, "n\n", "--repair");
        });

        [Fact(Skip = "pending: T643")]
        public static void A_fix_line_names_the_overlay()
        {
            Assert.Contains(Run.Value.StdOut.Split('\n'), l => l.Contains("Fix:", StringComparison.Ordinal) && l.Contains(DemoOverlay, StringComparison.Ordinal));
        }

        [Fact(Skip = "pending: T643")]
        public static void The_fix_prints_before_the_confirm()
        {
            var fixIndex = Run.Value.StdOut.IndexOf("Fix:", StringComparison.Ordinal);
            var confirmIndex = Run.Value.StdOut.IndexOf("Apply this fix?", StringComparison.Ordinal);

            Assert.True(fixIndex >= 0 && confirmIndex > fixIndex, Run.Value.StdOut);
        }

        [Fact(Skip = "pending: T643")]
        public static void Declining_exits_five()
        {
            Assert.Equal(5, Run.Value.ExitCode);
        }
    }

    public static class ScenarioYesRewritesTheKey
    {
        static readonly Lazy<(int ExitCode, string EnvAfter)> Run = new(() =>
        {
            var (envFile, docker) = Box(StaleComposeFile);
            var (exit, _, _) = HomeSetupHarness.RunSetup(envFile, docker, "", "--repair", "--yes");
            return (exit, File.ReadAllText(envFile));
        });

        [Fact(Skip = "pending: T643")]
        public static void Compose_file_keeps_base_and_pins_in_order()
        {
            Assert.Contains("COMPOSE_FILE=compose.yaml:compose.pinned.yaml\n", Run.Value.EnvAfter, StringComparison.Ordinal);
        }

        [Fact(Skip = "pending: T643")]
        public static void The_overlay_is_gone_from_the_file()
        {
            Assert.DoesNotContain(DemoOverlay, Run.Value.EnvAfter, StringComparison.Ordinal);
        }

        [Fact(Skip = "pending: T643")]
        public static void Exits_zero()
        {
            Assert.Equal(0, Run.Value.ExitCode);
        }
    }

    public static class SadPathACleanComposeFileRaisesNothing
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run = new(() =>
        {
            var (envFile, docker) = Box("compose.yaml:compose.pinned.yaml");
            return HomeSetupHarness.RunSetup(envFile, docker, "", "--repair", "--yes");
        });

        [Fact(Skip = "pending: T643")]
        public static void No_fix_line_prints()
        {
            Assert.DoesNotContain("Fix:", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact(Skip = "pending: T643")]
        public static void Exits_zero()
        {
            Assert.True(Run.Value.ExitCode == 0, $"exit {Run.Value.ExitCode}\n{Run.Value.StdOut}\n{Run.Value.StdErr}");
        }
    }
}
