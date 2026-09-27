// STORY-447 — Chaos: the stream survives an API outage and an engine restart (gh-#777 · SPEC F178.8 · PLAN T496/T497/T498)
//
// BDD specification — xUnit. The chaos leg of tools/gate/stack_gate.sh through GateHarness with
// the outage/recovery/capture budgets shortened (GATE_OUTAGE_SECS=1, GATE_RECOVERY_SECS=4,
// CAPTURE_SECS=1). The docker stub's metadata reader answers a safe frame until
// GATE_STUB_ONAIR_AFTER seconds after its first call (or never, with GATE_STUB_NEVER_ONAIR), so
// recovery is measured, not assumed; the fake station's /stream decides whether a capture has a
// silence event.
//
// PLAN T498 briefly bounded F178.8(a) to TOTAL outage silence (GATE_OUTAGE_SILENCE_MAX_SECS);
// F210's api-free safe resolver (gh-#791, PR #869) put it back to zero silence events and retired
// the knob — AC2/AC2b below. AC2b sets the retired knob anyway to prove a stale runner env can't
// loosen the bar, and gives the post-restart capture a CLEAN stream via
// GATE_STUB_STREAM_AFTER_RESTART so the one gap is the api-down window's alone.
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

    // AC2 — any silence during the outage fails (SPEC F178.8(a): zero silence events).
    public sealed class ScenarioAnyOutageSilenceFails : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6, withGap: true) };
        readonly Run run;

        public ScenarioAnyOutageSilenceFails() =>
            run = Chaos(station, new Dictionary<string, string>
            {
                ["GATE_STUB_ONAIR_AFTER"] = "0",
                ["CAPTURE_SECS"] = "8",
            });

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void ApiDownSilenceIsTheFirstFailingAssertion() => Assert.Equal("api-down silence", run.FirstFailure);
    }

    // ---------------------------------------------------------------------
    // AC2b — the retired T498 knob no longer loosens the bar, and the failure is still reported
    // ---------------------------------------------------------------------

    // The api-down window's own capture carries the one gap; the post-restart capture gets a clean
    // stream via GATE_STUB_STREAM_AFTER_RESTART, isolating the red to the api-down facts alone.
    public sealed class ScenarioAStaleOutageBoundIsIgnored : IDisposable
    {
        readonly FakeStation station = new() { StreamWav = MakeWav(-14, seconds: 6, withGap: true) };
        readonly Run run;

        public ScenarioAStaleOutageBoundIsIgnored()
        {
            var clean = MakeWav(-14, seconds: 6);
            run = Chaos(station, new Dictionary<string, string>
            {
                ["GATE_OUTAGE_SILENCE_MAX_SECS"] = "10",
                ["GATE_STUB_ONAIR_AFTER"] = "1",
                ["CAPTURE_SECS"] = "8",
                ["GATE_STUB_STREAM_AFTER_RESTART"] = clean,
            });
        }

        public void Dispose() => station.Dispose();

        [Fact]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void ApiDownSilenceIsTheFirstFailingAssertion() => Assert.Equal("api-down silence", run.FirstFailure);

        [Fact]
        public void TheEventCountIsPrinted() => Assert.Contains("api-down silence events: 1", run.ReportMd, StringComparison.Ordinal);

        [Fact]
        public void TheTotalIsPrinted() => Assert.Matches(@"api-down silence total: \d+(\.\d+)? s", run.ReportMd);

        [Fact]
        public void TheJsonCarriesTheCount() =>
            Assert.Equal(1, run.ReportJson?.RootElement.GetProperty("chaos").GetProperty("api_down_silence_events").GetInt32());
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
