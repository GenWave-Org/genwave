// STORY-444 — The stack gate runs a fresh install in a scratch and reports (gh-#777 · SPEC F178.1–F178.4, F178.9, F183.3 · PLAN T486/T487)
//
// BDD specification — xUnit. tools/gate/stack_gate.sh is driven the Story436 way: a scratch copy
// of the repo (planted .env canary, stubbed setup.sh/launch.sh/migrate.sh), a scripted docker on
// the scratch PATH logging every argv/cwd/env, and a loopback fake api (GateHarness). Budgets are
// shortened through the gate's GATE_*_SECS knobs — the wall-clock seat is the wire task (T488).
//
// RED at plan time: tools/gate/stack_gate.sh does not exist.

using System.Text.RegularExpressions;
using GenWave.Host.Tests.Support;
using static GenWave.Host.Tests.Support.GateHarness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheStackGateRunsAFreshInstallInAScratch
{

    // ---------------------------------------------------------------------
    // HAPPY PATH — the fresh leg against a healthy stub stack
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFreshLegAgainstAHealthyStubStack : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioAFreshLegAgainstAHealthyStubStack() =>
            run = Execute(station, args: ["--tag", "v9.9.9", "--fresh"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void TheLegPasses() => Assert.Equal(0, run.ExitCode);

        [Fact]
        public void TheCallersEnvNeverReachesDocker() => Assert.DoesNotContain(Canary, run.EnvLog, StringComparison.Ordinal);

        [Fact]
        public void TheScratchHasDbAndCompose() =>
            Assert.Contains(run.Calls, c => c.StartsWith("cwd=", StringComparison.Ordinal)
                && c.Contains(" db ", StringComparison.Ordinal) && c.Contains(" compose.yaml ", StringComparison.Ordinal));

        [Fact]
        public void TheScratchHasNoDotEnvOrDotGitCopiedIn()
        {
            // setup.sh legitimately writes $scratch/.env before every later docker call inside the
            // scratch, so only the FIRST cwd= line — the version probe, which runs before setup.sh
            // ever touches the scratch — can prove the rsync itself excluded .env/.git.
            var first = Array.Find(run.Calls, c => c.StartsWith("cwd=", StringComparison.Ordinal));
            Assert.NotNull(first);
            Assert.DoesNotContain(" .env ", first, StringComparison.Ordinal);
            Assert.DoesNotContain(" .git ", first, StringComparison.Ordinal);
        }

        [Fact]
        public void TheOverlayPinsExactlyFiveImagesToTheTag() =>
            Assert.Equal(5, Regex.Matches(run.Overlay, @"^\s*image:\s*\S+:home-v9\.9\.9\s*$", RegexOptions.Multiline).Count);

        [Fact]
        public void TheOverlayNamesTheFiveServices() =>
            Assert.DoesNotContain(new[] { "api:", "engine:", "icecast:", "admin_ui:", "piper:" },
                s => !run.Overlay.Contains(s, StringComparison.Ordinal));

        [Fact]
        public void TheFileSetIsBasePiperOnlyGate() =>
            Assert.Contains(run.Calls, c => c.Contains("files=compose.yaml:compose.piper-only.yaml:compose.gate.yaml", StringComparison.Ordinal));

        [Fact]
        public void TheProjectNameIsLegScoped() =>
            Assert.Matches(@"project=gw-gate-fresh-[0-9a-f]{8}(\s|$)", string.Join("\n", run.Calls));

        [Fact]
        public void SetupRanWithYes() => Assert.Contains(run.Calls, c => c.StartsWith("setup.sh --yes", StringComparison.Ordinal));

        [Fact]
        public void SetupWroteTheEnvInsideTheScratch()
        {
            var setup = Assert.Single(run.Calls, c => c.StartsWith("setup.sh ", StringComparison.Ordinal));
            var scratch = Regex.Match(string.Join("\n", run.Calls), @"^cwd=(\S+)", RegexOptions.Multiline).Groups[1].Value;
            Assert.Contains($"GW_ENV_FILE={scratch}/.env", setup, StringComparison.Ordinal);
        }

        [Fact]
        public void TheStackIsTornDownWithVolumes() =>
            Assert.Contains(run.Calls, c => c.Contains("compose", StringComparison.Ordinal) && c.EndsWith("down -v", StringComparison.Ordinal));

        [Fact]
        public void BothReportFilesExist() =>
            Assert.True(File.Exists(Path.Combine(run.ReportDir, "gate-report.md")) && File.Exists(Path.Combine(run.ReportDir, "gate-report.json")));

        [Fact]
        public void UpgradeIsReportedSkipped() =>
            Assert.Contains("upgrade | skipped (--upgrade not given)", run.ReportMd, StringComparison.Ordinal);

        [Fact]
        public void CaptureIsReportedSkipped() =>
            Assert.Contains("capture | skipped (--capture not given)", run.ReportMd, StringComparison.Ordinal);

        [Fact]
        public void ChaosIsReportedSkipped() =>
            Assert.Contains("chaos | skipped (--chaos not given)", run.ReportMd, StringComparison.Ordinal);

        [Fact]
        public void TheManualEvidenceLineIsFixedText() =>
            Assert.Contains("Needs manual evidence: ", run.ReportMd, StringComparison.Ordinal);

        [Fact]
        public void TheManualFactCountComesFromTheCheckout()
        {
            var testsRoot = Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tests");
            // Mirrors tools/gate/stack_gate.sh's own count_manual_facts (SPEC F182.3): Skip= FACT
            // sites, not a blind `"manual: ` substring count — several facts in the same
            // acceptance-gate file routinely share one `const string Skip = "manual: ..."`
            // (Story443's own new fact pins this bash count against the real scanner exactly;
            // this one just proves the REPORTED number really came from scanning the checkout).
            var expected = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != "Story443_SkipPrefixLaw.cs")
                .Sum(CountManualFactsInFile);
            Assert.Contains($"Needs manual evidence: {expected} facts need a person listening to the stream", run.ReportMd, StringComparison.Ordinal);
        }

        static int CountManualFactsInFile(string file)
        {
            var flat = Regex.Replace(File.ReadAllText(file), @"\s+", " ");
            var manualNames = Regex.Matches(flat, "const string (\\w+)\\s*=\\s*\"manual: ")
                .Select(m => m.Groups[1].Value)
                .Distinct();

            var count = Regex.Matches(flat, @"\[(Fact|Theory)\([^)]*?\bSkip\s*=\s*""manual: ").Count;
            foreach (var name in manualNames)
            {
                count += Regex.Matches(flat, $@"\[(Fact|Theory)\([^)]*?\bSkip\s*=\s*{Regex.Escape(name)}\b").Count;
            }

            return count;
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — usage
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoTagIsUsage : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioNoTagIsUsage() => run = Execute(station, args: ["--fresh"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsTwo() => Assert.Equal(2, run.ExitCode);

        [Fact]
        public void StderrNamesTheFlag() => Assert.Contains("--tag", run.StdErr, StringComparison.Ordinal);
    }

    public sealed class ScenarioAnUnknownFlagIsUsage : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioAnUnknownFlagIsUsage() => run = Execute(station, args: ["--tag", "v9.9.9", "--bogus"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsTwo() => Assert.Equal(2, run.ExitCode);
    }

    public sealed class ScenarioAMissingPrerequisiteIsExitTwo : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioAMissingPrerequisiteIsExitTwo()
        {
            var bin = MakeBinDir();
            File.Delete(Path.Combine(bin, "ffmpeg"));
            run = Execute(station, bin, args: ["--tag", "v9.9.9", "--fresh"]);
        }

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsTwo() => Assert.Equal(2, run.ExitCode);

        [Fact]
        public void StderrNamesFfmpeg() => Assert.Contains("ffmpeg", run.StdErr, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a failing leg still tears down and reports
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTrapTearsDownWhenComposeUpFails : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioTheTrapTearsDownWhenComposeUpFails() =>
            run = Execute(station, env: new Dictionary<string, string> { ["GATE_STUB_UP_EXIT"] = "1" }, args: ["--tag", "v9.9.9", "--fresh"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void DownRunsAfterTheFailedUp() =>
            Assert.True(run.IndexOf("down -v") > run.IndexOf("up files="), string.Join("\n", run.Calls));

        [Fact]
        public void DownNamesTheLegProject() =>
            Assert.Matches(@"-p gw-gate-fresh-[0-9a-f]{8} .*down -v", string.Join("\n", run.Calls));
    }

    public sealed class ScenarioAHealthMissFailsTheLegWithLogs : IDisposable
    {
        readonly FakeStation station = new() { HealthStatus = 503 };
        readonly Run run;

        public ScenarioAHealthMissFailsTheLegWithLogs() => run = Execute(station, args: ["--tag", "v9.9.9", "--fresh"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void HealthIsTheFirstFailingAssertion() => Assert.Equal("health", run.FirstFailure);

        [Fact]
        public void ComposeLogsAreAttached() => Assert.True(File.Exists(Path.Combine(run.ReportDir, "compose-fresh.log")));
    }
}
