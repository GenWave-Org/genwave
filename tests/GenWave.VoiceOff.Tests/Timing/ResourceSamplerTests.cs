using GenWave.VoiceOff.Tests.Support;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class ResourceSamplerTests
{
    [Fact]
    public void TheDockerArgvIsReadOnlyStatsWithTheNameAfterTheDoubleDash() =>
        Assert.Equal(
            ["stats", "--no-stream", "--format", "{{.MemUsage}}", "--", "genwave-kokoro-1"],
            ProcessResourceProbe.Arguments("genwave-kokoro-1"));

    [Fact]
    public async Task TheSamplerTicksEveryOneSecond()
    {
        ManualTimeProvider clock = new();
        await ScriptedResourceProbe.SampleAsync(["1MiB / 4GiB", "2MiB / 4GiB"], ["0"], clock);
        Assert.Equal([TimeSpan.FromSeconds(1)], clock.DueTimes);
    }

    [Fact]
    public async Task AHostWithoutAGpuReportsVramUnknown()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["1MiB", "1MiB"], [null, null, null]);
        Assert.Null(usage.PeakVramMb);
    }

    [Fact]
    public async Task AFailedContainerReadIsSkippedAndTheRunKeepsGoing()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["200MiB / 4GiB", null, "300MiB / 4GiB"], ["0"]);
        Assert.Equal(300, usage.PeakRssMb);
    }

    [Fact]
    public async Task AnUnparseableContainerReadIsSkippedAndTheRunKeepsGoing()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["200MiB / 4GiB", "garbage", "300MiB / 4GiB"], ["0"]);
        Assert.Equal(300, usage.PeakRssMb);
    }

    [Fact]
    public async Task AProbeThatThrowsOnOneSampleIsSkippedAndTheRunKeepsGoing()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(
            ["200MiB / 4GiB", "999MiB / 4GiB", "300MiB / 4GiB"], ["0"], wrap: inner => new ThrowingResourceProbe(inner, containerRead: 1));
        Assert.Equal(300, usage.PeakRssMb);
    }

    [Fact]
    public async Task AThrowingBaselineReadLeavesVramUnknown()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(
            ["1MiB", "1MiB"], ["800", "3600", "3600"], wrap: inner => new ThrowingResourceProbe(inner, gpuRead: 0));
        Assert.Null(usage.PeakVramMb);
    }

    [Fact]
    public async Task TheFirstContainerReadHappensBeforeAnyTimerFires()
    {
        using CancellationTokenSource window = new();
        ResourceSampler sampler = new(new ScriptedResourceProbe(["7MiB / 4GiB"], ["0"], window), "engine-box", new ManualTimeProvider());
        await sampler.RunAsync(window.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(7, sampler.Usage.PeakRssMb);
    }

    [Fact]
    public async Task NothingReadMeansRssUnknown()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync([null], []);
        Assert.Null(usage.PeakRssMb);
    }

    [Fact]
    public async Task TheProbeIsAskedAboutTheNamedContainer()
    {
        List<string> asked = [];
        await ScriptedResourceProbe.SampleAsync(["1MiB"], [], asked: asked);
        Assert.Equal(["engine-box"], asked);
    }

    [Fact]
    public async Task CancellingBeforeTheFirstSampleReturnsWithoutThrowing()
    {
        using CancellationTokenSource window = new();
        await window.CancelAsync();
        ResourceSampler sampler = new(new ScriptedResourceProbe([], [], window), "engine-box", new ManualTimeProvider());
        await sampler.RunAsync(window.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new ResourceUsage(null, null), sampler.Usage);
    }
}
