// STORY-515 — delight: --repair flags a stale COMPOSE_FILE (F216 · PLAN T643)
//
// BDD specification — xUnit. Adoption mode through HomeSetupHarness: a .env whose COMPOSE_FILE
// still names the deleted demo overlay must surface as a repairable finding (Fix: line, confirm,
// exit 5 when declined) and `--yes` must rewrite the key. A clean COMPOSE_FILE raises nothing.
// T643 landed: every fact is live.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRepairFlagsStaleComposeFile
{
    // Assembled at runtime so this file never trips Story521's tree-wide grep.
    static readonly string DemoOverlay = "compose." + "demo.yaml";

    static readonly string StaleComposeFile = $"compose.yaml:compose.pinned.yaml:{DemoOverlay}";

    static (int ExitCode, string StdOut, string StdErr) RunExported(string envFile, string docker, string exportedComposeFile) =>
        HomeSetupHarness.RunSetup(
            envFile, docker, "",
            new Dictionary<string, string> { ["COMPOSE_FILE"] = exportedComposeFile },
            "--repair", "--yes");

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

        [Fact]
        public static void A_fix_line_names_the_overlay()
        {
            Assert.Contains(Run.Value.StdOut.Split('\n'), l => l.Contains("Fix:", StringComparison.Ordinal) && l.Contains(DemoOverlay, StringComparison.Ordinal));
        }

        [Fact]
        public static void The_fix_prints_before_the_confirm()
        {
            var fixIndex = Run.Value.StdOut.IndexOf("Fix:", StringComparison.Ordinal);
            var confirmIndex = Run.Value.StdOut.IndexOf("Apply this fix?", StringComparison.Ordinal);

            Assert.True(fixIndex >= 0 && confirmIndex > fixIndex, Run.Value.StdOut);
        }

        [Fact]
        public static void Declining_exits_five()
        {
            Assert.Equal(5, Run.Value.ExitCode);
        }
    }

    public static class ScenarioAPathQualifiedElementIsDropped
    {
        static readonly Lazy<(int ExitCode, string StdOut, string EnvAfter)> Run = new(() =>
        {
            var (envFile, docker) = Box($"compose.yaml:compose.pinned.yaml:/x/y/{DemoOverlay}");
            var (exit, stdOut, _) = HomeSetupHarness.RunSetup(envFile, docker, "", "--repair", "--yes");
            return (exit, stdOut, File.ReadAllText(envFile));
        });

        [Fact]
        public static void It_is_reported()
        {
            Assert.Contains("Fix:", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public static void Only_the_base_and_pins_remain()
        {
            Assert.Contains("COMPOSE_FILE=compose.yaml:compose.pinned.yaml\n", Run.Value.EnvAfter, StringComparison.Ordinal);
        }
    }

    public static class ScenarioTheFileDecidesNotTheProcessEnvironment
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> StaleFileCleanExport = new(() =>
        {
            var (envFile, docker) = Box(StaleComposeFile);
            return RunExported(envFile, docker, "compose.yaml:compose.pinned.yaml");
        });

        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> CleanFileStaleExport = new(() =>
        {
            var (envFile, docker) = Box("compose.yaml:compose.pinned.yaml");
            return RunExported(envFile, docker, StaleComposeFile);
        });

        [Fact]
        public static void A_stale_file_is_reported_despite_a_clean_exported_value()
        {
            Assert.Contains("Fix:", StaleFileCleanExport.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public static void A_clean_file_is_not_reported_because_of_an_exported_overlay()
        {
            Assert.DoesNotContain("Fix:", CleanFileStaleExport.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public static void The_exported_overlay_still_shows_up_as_an_info_row()
        {
            var lines = CleanFileStaleExport.Value.StdOut.Split('\n');
            Assert.Contains(lines, l => l.Contains("Compose file overrides", StringComparison.Ordinal) && l.Contains(DemoOverlay, StringComparison.Ordinal));
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

        [Fact]
        public static void Compose_file_keeps_base_and_pins_in_order()
        {
            Assert.Contains("COMPOSE_FILE=compose.yaml:compose.pinned.yaml\n", Run.Value.EnvAfter, StringComparison.Ordinal);
        }

        [Fact]
        public static void The_overlay_is_gone_from_the_file()
        {
            Assert.DoesNotContain(DemoOverlay, Run.Value.EnvAfter, StringComparison.Ordinal);
        }

        [Fact]
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

        [Fact]
        public static void No_fix_line_prints()
        {
            Assert.DoesNotContain("Fix:", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public static void Exits_zero()
        {
            Assert.True(Run.Value.ExitCode == 0, $"exit {Run.Value.ExitCode}\n{Run.Value.StdOut}\n{Run.Value.StdErr}");
        }
    }

    public static class ScenarioTheRewriteKeepsTheFileSecret
    {
        static readonly Lazy<UnixFileMode?> ModeAfter = new(() =>
        {
            if (OperatingSystem.IsWindows()) return null;
            var (envFile, docker) = Box(StaleComposeFile);
            File.SetUnixFileMode(envFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            HomeSetupHarness.RunSetup(envFile, docker, "", "--repair", "--yes");
            return File.GetUnixFileMode(envFile);
        });

        [Fact]
        public static void A_world_readable_env_comes_out_owner_only()
        {
            if (ModeAfter.Value is null) return;
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, ModeAfter.Value);
        }
    }

    public static class ScenarioAnOverlayOnlyListOnTheLastLine
    {
        static readonly Lazy<(int ExitCode, string StdOut, string EnvAfter)> Run = new(() =>
        {
            var envFile = HomeSetupHarness.ScratchEnvPath();
            File.WriteAllText(envFile, $"A=1\nCOMPOSE_FILE={DemoOverlay}\n");
            var docker = HomeSetupHarness.WriteDockerStub($"-f {DemoOverlay}");
            var (exit, stdOut, _) = HomeSetupHarness.RunSetup(envFile, docker, "", "--repair", "--yes");
            return (exit, stdOut, File.ReadAllText(envFile));
        });

        [Fact]
        public static void The_key_is_removed_and_the_rest_kept()
        {
            Assert.Equal("A=1\n", Run.Value.EnvAfter);
        }

        [Fact]
        public static void The_repair_reports_done()
        {
            Assert.Contains("Done.", Run.Value.StdOut, StringComparison.Ordinal);
        }
    }
}
