// STORY-493 — Render scores speed and footprint (gh-#614 · SPEC F214.7, F214.8 · PLAN T610, T611)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

using System.Text.RegularExpressions;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;
using GenWave.VoiceOff.Timing;

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
            Assert.Equal(0.5, new TimingRecord("alpha", RenderLane.Plain, "hard-01", EngineTier.Cpu, 2000, 4000, null, null, true, null).Rtf);
    }

    // AC2 — median and p95
    public sealed class ScenarioMedianAndP95
    {
        // Given rtf values 1..20 for one engine
        // When  the summary is computed

        private static RtfSummary Summary() =>
            RtfSummary.From(Enumerable.Range(1, 20).Select(i =>
                new TimingRecord("alpha", RenderLane.Plain, $"line-{i}", EngineTier.Cpu, i * 1000, 1000, null, null, true, null)))
            ?? throw new InvalidOperationException("expected a summary");

        [Fact]
        public void MedianIs105() => Assert.Equal(10.5, Summary().Median, 10);

        [Fact]
        public void P95Is1905() => Assert.Equal(19.05, Summary().P95, 10);
    }

    // AC3 — RSS peak from docker stats
    public sealed class ScenarioRSSPeakFromDockerStats
    {
        // Given docker stats lines reading 512MiB, 1.2GiB, 900MiB
        // When  the sampler is parsed

        [Fact]
        public async Task PeakRSSIs1229MB()
        {
            ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["512MiB / 4GiB", "1.2GiB / 4GiB", "900MiB / 4GiB"], ["0"]);
            Assert.Equal(1229, usage.PeakRssMb);
        }
    }

    // AC4 — VRAM is the container's peak
    public sealed class ScenarioVRAMIsTheContainersPeak
    {
        // Given the container's processes 101, 102 and GPU samples where they use 800, 3600, 3100 MiB in total (other processes ignored)
        // When  the sampler is parsed

        [Fact]
        public async Task PeakVRAMIs3600MB()
        {
            ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(
                ["1MiB", "1MiB", "1MiB"],
                [
                    $"101, 500\n{ScriptedResourceProbe.ForeignGpuLine}\n102, 300",
                    $"101, 1800\n102, 1800\n{ScriptedResourceProbe.ForeignGpuLine}",
                    $"{ScriptedResourceProbe.ForeignGpuLine}\n101, 3000\n102, 100",
                ]);
            Assert.Equal(3600, usage.PeakVramMb);
        }
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
            Assert.Equal(6, run.OutputLines.Count(l => Regex.IsMatch(l, @"^(alpha|beta) plain [a-z0-9-]+ rtf \d+\.\d\d$")));

        [Fact]
        public void ASummaryPerEngine() =>
            Assert.Equal(
                ["alpha: 3 clips, 0 failed", "beta: 3 clips, 0 failed"],
                run.OutputLines.Where(l => l.Contains(" clips, ", StringComparison.Ordinal)).Select(l => l[..l.IndexOf(", rtf", StringComparison.Ordinal)]));

        public void Dispose() => scenario.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — no GPU on the cpu tier
    public sealed class ScenarioNoGPUOnTheCpuTier : IDisposable
    {
        // Given `render --tier cpu` on a host with no nvidia-smi
        // When  the run finishes

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioNoGPUOnTheCpuTier()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
        }

        [Fact]
        public void VRAMIs() => Assert.Contains(run.OutputLines, l => l.StartsWith("alpha: ", StringComparison.Ordinal) && l.Contains("VRAM —,", StringComparison.Ordinal));

        [Fact]
        public void NvidiaSmiIsNeverAsked() => Assert.Equal(0, run.GpuReads);

        [Fact]
        public void TheRunIsOk() => Assert.Equal(0, run.Exit);

        public void Dispose() => scenario.Dispose();
    }
}
