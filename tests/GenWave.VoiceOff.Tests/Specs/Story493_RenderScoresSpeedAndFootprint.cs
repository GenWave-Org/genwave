// STORY-493 — Render scores speed and footprint (gh-#614 · SPEC F214.7, F214.8 · PLAN T610, T611)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

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

    // AC4 — VRAM is the GPU delta
    public sealed class ScenarioVRAMIsTheGPUDelta
    {
        // Given nvidia-smi baseline 800 MiB and samples 800, 3600, 3100
        // When  the sampler is parsed

        [Fact]
        public async Task VRAMDeltaIs2800MB()
        {
            ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["1MiB", "1MiB", "1MiB"], ["800", "800", "3600", "3100"]);
            Assert.Equal(2800, usage.PeakVramMb);
        }
    }

    // AC5 — the tier is a run fact
    public sealed class ScenarioTheTierIsARunFact
    {
        // Given `render --tier cpu`
        // When  timing.jsonl is written

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void EveryRecordSaysTierCpu() => Assert.Fail("pending: T611 — every record says tier 'cpu'");
    }

    // AC6 — engines skip tiers they don't attempt
    public sealed class ScenarioEnginesSkipTiersTheyDonTAttempt
    {
        // Given a profile with tiers ["gpu"]
        // When  `render --tier cpu` runs

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void NoRequestReachesThatEngine() => Assert.Fail("pending: T611 — no request reaches that engine");

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void ItsRowSaysNotAttempted() => Assert.Fail("pending: T611 — its row says 'not attempted'");
    }

    // AC7 — one line per clip
    public sealed class ScenarioOneLinePerClip
    {
        // Given a run of 2 engines × 3 lines
        // When  render runs

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void StdoutCarries6ClipLinesEngineLineIdRtf() => Assert.Fail("pending: T611 — stdout carries 6 clip lines (engine, line id, rtf)");

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void ASummaryPerEngine() => Assert.Fail("pending: T611 — a summary per engine");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — no GPU on the cpu tier
    public sealed class ScenarioNoGPUOnTheCpuTier
    {
        // Given `render --tier cpu` on a host with no nvidia-smi
        // When  the run finishes

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void VRAMIs() => Assert.Fail("pending: T611 — VRAM is '—'");

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void TheRunIsOk() => Assert.Fail("pending: T611 — the run is ok");
    }
}
