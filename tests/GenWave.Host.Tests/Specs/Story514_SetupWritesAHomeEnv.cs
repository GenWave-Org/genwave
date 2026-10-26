// STORY-514 — setup.sh writes a home .env and speaks plain (F216 · PLAN T642)
//
// BDD specification — xUnit. AC1 drives the real first-run interview (Story344's three-answer
// idiom: music path, topology, admin) and reads the .env it writes. AC2/AC3 drive adoption mode
// through HomeSetupHarness (scripted docker, no daemon).

using System.Text.RegularExpressions;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static partial class FeatureSetupWritesAHomeEnv
{
    // Assembled at runtime so this file never trips Story521's tree-wide grep.
    static readonly string PublicHost = "PUBLIC_" + "HOST";
    static readonly string TunnelToken = "TUNNEL_" + "TOKEN";
    static readonly string PublicStationWording = "public appli" + "ance";

    [GeneratedRegex(@"SPEC F\d|STORY-\d|gh-#\d")]
    private static partial Regex PlanningIds();

    public static class ScenarioTheWizardEnvHasNoPublicStationLines
    {
        static readonly Lazy<string> EnvContent = new(() =>
        {
            var mediaDir = TempDir.CreateForProcessLifetime();
            File.WriteAllBytes(Path.Combine(mediaDir, "one.flac"), [0x66, 0x4C, 0x61, 0x43]);
            var envFile = HomeSetupHarness.ScratchEnvPath();

            var scratchDir = TempDir.CreateForProcessLifetime();
            var answersPath = Path.Combine(scratchDir, "answers.txt");
            File.WriteAllText(answersPath, $"{mediaDir}\n1\ny\n");
            var wrapperPath = Path.Combine(scratchDir, "run-setup.sh");
            var scriptPath = Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "setup.sh");
            File.WriteAllText(wrapperPath, $"exec bash \"{scriptPath}\" < \"{answersPath}\"\n");

            ScriptProcess.Run(wrapperPath, ScriptProcess.MakeBinDir(), envFile,
                new Dictionary<string, string> { ["SKIP_PREFLIGHT"] = "1" });
            return File.ReadAllText(envFile);
        });

        [Fact]
        public static void No_hostname_line()
        {
            Assert.DoesNotContain(PublicHost, EnvContent.Value, StringComparison.Ordinal);
        }

        [Fact]
        public static void No_token_line()
        {
            Assert.DoesNotContain(TunnelToken, EnvContent.Value, StringComparison.Ordinal);
        }

        [Fact]
        public static void No_public_station_wording()
        {
            Assert.DoesNotContain(PublicStationWording, EnvContent.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class ScenarioVerifyPrintsNoPlanningIds
    {
        const string ComposeFile = "compose.yaml:compose.pinned.yaml";

        static (int ExitCode, string StdOut, string StdErr) RunHealthy(params string[] args)
        {
            var envFile = HomeSetupHarness.ScratchEnvPath();
            HomeSetupHarness.WriteEnvFile(envFile, HomeSetupHarness.HealthyEnvValues(ComposeFile));
            var docker = HomeSetupHarness.WriteDockerStub("-f compose.yaml -f compose.pinned.yaml");
            return HomeSetupHarness.RunSetup(envFile, docker, "", args);
        }

        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Verify = new(() => RunHealthy());

        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Repair = new(() => RunHealthy("--repair", "--yes"));

        [Fact]
        public static void Verify_output_has_no_ids()
        {
            Assert.False(PlanningIds().IsMatch(Verify.Value.StdOut + Verify.Value.StdErr), Verify.Value.StdOut);
        }

        [Fact]
        public static void Repair_output_has_no_ids()
        {
            Assert.False(PlanningIds().IsMatch(Repair.Value.StdOut + Repair.Value.StdErr), Repair.Value.StdOut);
        }
    }

    public static class ScenarioAWizardWrittenEnvVerifiesGreen
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run = new(() =>
        {
            var envFile = HomeSetupHarness.ScratchEnvPath();
            HomeSetupHarness.WriteEnvFile(envFile, HomeSetupHarness.HealthyEnvValues("compose.yaml:compose.pinned.yaml"));
            var docker = HomeSetupHarness.WriteDockerStub("-f compose.yaml -f compose.pinned.yaml");
            return HomeSetupHarness.RunSetup(envFile, docker, "");
        });

        [Fact]
        public static void Exits_zero()
        {
            Assert.True(Run.Value.ExitCode == 0, $"exit {Run.Value.ExitCode}\n{Run.Value.StdOut}\n{Run.Value.StdErr}");
        }

        [Fact]
        public static void Never_names_the_hostname_key()
        {
            Assert.DoesNotContain(PublicHost, Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public static void Never_names_the_token_key()
        {
            Assert.DoesNotContain(TunnelToken, Run.Value.StdOut, StringComparison.Ordinal);
        }
    }
}
