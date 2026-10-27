// STORY-493 — Render scores speed (the footprint facts left with the resource sampler in T660) (gh-#614 · SPEC F214.7, F214.8 · PLAN T610, T611)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

using System.Text.RegularExpressions;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureRenderScoresSpeedAndFootprint
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — rtf per render
    public sealed class ScenarioRtfPerRender
    {
        // Given a render of 2000 ms wall clock producing 4000 ms of audio
        // When  the timing record is written

        [Fact]
        public void RtfIs05() =>
            Assert.Equal(0.5, new TimingRecord("alpha", "hard-01", EngineTier.Cpu, 2000, 4000, true, null).Rtf);
    }

    // AC2 — median and p95
    public sealed class ScenarioMedianAndP95
    {
        // Given rtf values 1..20 for one engine
        // When  the summary is computed

        private static RtfSummary Summary() =>
            RtfSummary.From(Enumerable.Range(1, 20).Select(i =>
                new TimingRecord("alpha", $"line-{i}", EngineTier.Cpu, i * 1000, 1000, true, null)))
            ?? throw new InvalidOperationException("expected a summary");

        [Fact]
        public void MedianIs105() => Assert.Equal(10.5, Summary().Median, 10);

        [Fact]
        public void P95Is1905() => Assert.Equal(19.05, Summary().P95, 10);
    }

    // AC5 — the tier is a run fact
    public sealed class ScenarioTheTierIsARunFact : IDisposable
    {
        // Given `render --tier cpu`
        // When  timing.jsonl is written

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioTheTierIsARunFact()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
        }

        [Fact]
        public async Task EveryRecordSaysTierCpu()
        {
            IReadOnlyList<TimingRecord> records = await new TimingLog(run.PathIn("timing.jsonl")).ReadAsync(CancellationToken.None);
            Assert.Equal([EngineTier.Cpu], records.Select(r => r.Tier).Distinct());
        }

        public void Dispose() => scenario.Dispose();
    }

    // AC6 — engines skip tiers they don't attempt
    public sealed class ScenarioEnginesSkipTiersTheyDonTAttempt : IDisposable
    {
        // Given a profile with tiers ["gpu"]
        // When  `render --tier cpu` runs

        private readonly RenderScenario scenario = new();
        private readonly EngineStubServer stub;
        private readonly FakeDockerInspector docker = new(new ContainerState(true, true, "10.0.0.2"));
        private readonly RenderRun run;

        public ScenarioEnginesSkipTiersTheyDonTAttempt()
        {
            stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri, tiers: ["gpu"]);
            run = scenario.Run(docker, scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
        }

        [Fact]
        public void NoRequestReachesThatEngine() => Assert.Equal(0, stub.Requests.TotalRequests);

        [Fact]
        public void NoDockerCallIsMadeForIt() => Assert.Empty(docker.Asked);

        [Fact]
        public void ItsRowSaysNotAttempted() =>
            Assert.Contains("alpha: not attempted (tier cpu not in its tiers)", run.OutputLines);

        public void Dispose() => scenario.Dispose();
    }

    // AC7 — one line per clip
    public sealed class ScenarioOneLinePerClip : IDisposable
    {
        // Given a run of 2 engines × 3 lines
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioOneLinePerClip()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddEngine("beta", stub.BaseUri);
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
        }

        [Fact]
        public void StdoutCarries6ClipLinesEngineLineIdRtf() =>
            Assert.Equal(6, run.OutputLines.Count(l => Regex.IsMatch(l, @"^(alpha|beta) [a-z0-9-]+ rtf \d+\.\d\d$")));

        [Fact]
        public void ASummaryPerEngine() =>
            Assert.Equal(
                ["alpha: 3 clips, 0 failed", "beta: 3 clips, 0 failed"],
                run.OutputLines.Where(l => l.Contains(" clips, ", StringComparison.Ordinal)).Select(l => l[..l.IndexOf(", rtf", StringComparison.Ordinal)]));

        public void Dispose() => scenario.Dispose();
    }
}
