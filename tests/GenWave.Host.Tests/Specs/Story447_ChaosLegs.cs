// STORY-447 — Chaos: the stream survives an API outage and an engine restart (gh-#777 · SPEC F178.8 · PLAN T496/T497)
//
// BDD specification — xUnit. The chaos leg of tools/gate/stack_gate.sh through GateHarness with
// the outage/recovery/capture budgets shortened (GATE_OUTAGE_SECS=1, GATE_RECOVERY_SECS=4,
// CAPTURE_SECS=1). The docker stub's metadata reader answers a safe frame until
// GATE_STUB_ONAIR_AFTER seconds after its first call (or never, with GATE_STUB_NEVER_ONAIR), so
// recovery is measured, not assumed; the fake station's /stream decides whether a capture has a
// silence event.
//
// RED at plan time: tools/gate/stack_gate.sh does not exist.

using GenWave.Host.Tests.Support;
using static GenWave.Host.Tests.Support.GateHarness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheStreamSurvivesAnApiOutageAndAnEngineRestart
{
    static Run Chaos(FakeStation station, IReadOnlyDictionary<string, string>? env = null) =>
        Execute(station, null, env, ["--tag", "v9.9.9", "--fresh", "--capture", "--chaos"]);

    // ---------------------------------------------------------------------
    // HAPPY PATH — both scenarios pass against a clean stream
    // ---------------------------------------------------------------------

    public sealed class ScenarioACleanStreamPassesBothScenarios : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6) };
        readonly Run run;

        public ScenarioACleanStreamPassesBothScenarios() =>
            run = Chaos(station, new Dictionary<string, string> { ["GATE_STUB_ONAIR_AFTER"] = "1" });

        public void Dispose() => station.Dispose();

        [Fact]
        public void TheLegPasses() => Assert.Equal(0, run.ExitCode);

        [Fact]
        public void TheApiIsStopped() => Assert.Contains(run.Calls, c => c.EndsWith("stop api", StringComparison.Ordinal));

        [Fact]
        public void TheApiIsStartedAfterTheStop() =>
            Assert.True(run.IndexOf("stop api") < run.IndexOf("start api"), string.Join("\n", run.Calls));

        [Fact]
        public void RecoveryIsMeasuredInSeconds() =>
            Assert.InRange(run.ReportJson?.RootElement.GetProperty("chaos").GetProperty("api_down_recovery_seconds").GetInt32() ?? -1, 0, 4);

        [Fact]
        public void RecoveryIsPrinted() => Assert.Matches(@"api-down recovery: \d+ s", run.ReportMd);

        [Fact]
        public void TheEngineIsRestarted() => Assert.Contains(run.Calls, c => c.EndsWith("restart engine", StringComparison.Ordinal));

        [Fact]
        public void TheEngineRestartFollowsTheApiRecovery() =>
            Assert.True(run.IndexOf("start api") < run.IndexOf("restart engine"), string.Join("\n", run.Calls));

        [Fact]
        public void TheEngineGapIsPrinted() => Assert.Matches(@"engine-reconnect on-air: \d+ s", run.ReportMd);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the api-down scenario
    // ---------------------------------------------------------------------

    public sealed class ScenarioSilenceDuringTheOutageFails : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6, withGap: true) };
        readonly Run run;

        public ScenarioSilenceDuringTheOutageFails() =>
            run = Chaos(station, new Dictionary<string, string> { ["GATE_STUB_ONAIR_AFTER"] = "0", ["CAPTURE_SECS"] = "8" });

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void ApiDownSilenceIsTheFirstFailingAssertion() => Assert.Equal("api-down silence", run.FirstFailure);
    }

    public sealed class ScenarioSlowRecoveryFails : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6) };
        readonly Run run;

        // On air for the fresh leg, dark once `stop api` is logged (GateHarness's docker stub).
        public ScenarioSlowRecoveryFails() =>
            run = Chaos(station, new Dictionary<string, string> { ["GATE_STUB_ONAIR_AFTER"] = "0", ["GATE_STUB_NEVER_ONAIR_AFTER_STOP"] = "1" });

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void ApiDownRecoveryIsTheFirstFailingAssertion() => Assert.Equal("api-down recovery", run.FirstFailure);
    }

    public sealed class ScenarioChaosWithoutCaptureIsUsage : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioChaosWithoutCaptureIsUsage() => run = Execute(station, args: ["--tag", "v9.9.9", "--fresh", "--chaos"]);

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsTwo() => Assert.Equal(2, run.ExitCode);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the engine-reconnect scenario
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoFrameAfterTheRestartFails : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6) };
        readonly Run run;

        public ScenarioNoFrameAfterTheRestartFails() =>
            run = Chaos(station, new Dictionary<string, string> { ["GATE_STUB_ONAIR_AFTER"] = "0", ["GATE_STUB_NEVER_ONAIR_AFTER_RESTART"] = "1" });

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void EngineReconnectOnAirIsTheFirstFailingAssertion() => Assert.Equal("engine-reconnect on-air", run.FirstFailure);
    }

    public sealed class ScenarioADirtyPostRestartCaptureFails : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6) };
        readonly Run run;

        public ScenarioADirtyPostRestartCaptureFails()
        {
            // The api-down window records the clean tone; the stream flips to the gapped file the
            // moment the engine restart is logged (the station watches the stub log).
            var gapped = MakeWav(-14, seconds: 6, withGap: true);
            run = Chaos(station, new Dictionary<string, string>
            {
                ["GATE_STUB_ONAIR_AFTER"] = "0",
                ["GATE_STUB_STREAM_AFTER_RESTART"] = gapped,
            });
        }

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void EngineReconnectSilenceIsTheFirstFailingAssertion() => Assert.Equal("engine-reconnect silence", run.FirstFailure);
    }
}
