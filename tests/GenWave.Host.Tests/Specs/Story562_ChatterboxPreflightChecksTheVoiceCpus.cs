// STORY-562 — Chatterbox ships with the station (SPEC F219.28 · PLAN T771)
//
// BDD specification — xUnit. tools/preflight.sh's preflight_voice_cpus: compose refuses a `cpus:` above
// the host's core count, so GENWAVE_VOICE_CPUS is checked before launch.sh touches anything. Drives the
// real function from an EMPTY environment (nothing a CI runner exports can leak in), with the core count
// stubbed through the GW_NPROC_CMD seam and GENWAVE_VOICE_CPUS supplied by an isolated env file.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureChatterboxPreflightChecksTheVoiceCpus
{
    const int StubbedCores = 8;

    static (int ExitCode, string StdOut, string StdErr) RunCheck(string topology, string voiceCpus)
    {
        var scratch = TempDir.CreateForProcessLifetime();
        var envFile = Path.Combine(scratch, "test.env");
        File.WriteAllText(envFile, $"GENWAVE_VOICE_CPUS={voiceCpus}\n");

        var bin = ScriptProcess.MakeBinDir();
        var nproc = Path.Combine(bin, "nproc-stub");
        ScriptProcess.AddStub(bin, "nproc-stub", $"echo {StubbedCores}");

        var script = Path.Combine(scratch, "run.sh");
        File.WriteAllText(script, "set -euo pipefail; . tools/preflight.sh; preflight_voice_cpus");

        return ScriptProcess.RunWithEmptyEnvironment(script, bin, new Dictionary<string, string>
        {
            ["GW_ENV_FILE"] = envFile,
            ["GW_NPROC_CMD"] = nproc,
            ["GW_PREFLIGHT_TOPOLOGY"] = topology,
        });
    }

    public sealed class ScenarioANonNumberIsRefused
    {
        readonly (int ExitCode, string StdOut, string StdErr) result = RunCheck("chatterbox", "lots");

        [Fact]
        public void PreflightExitsWithCode3() => Assert.Equal(3, result.ExitCode);
    }

    public sealed class ScenarioMoreCoresThanTheMachineHas
    {
        readonly (int ExitCode, string StdOut, string StdErr) result = RunCheck("chatterbox", (StubbedCores + 1).ToString());

        [Fact]
        public void PreflightExitsWithCode3() => Assert.Equal(3, result.ExitCode);
    }

    public sealed class ScenarioACountTheMachineHas
    {
        readonly (int ExitCode, string StdOut, string StdErr) result = RunCheck("chatterbox", StubbedCores.ToString());

        [Fact]
        public void PreflightPasses() => Assert.Contains("PASS  Voice CPUs", result.StdOut);
    }

    public sealed class ScenarioAnotherTopology
    {
        readonly (int ExitCode, string StdOut, string StdErr) result = RunCheck("full", "lots");

        [Fact]
        public void ABadValueIsNotChecked() => Assert.Equal(0, result.ExitCode);
    }
}
