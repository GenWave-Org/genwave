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

    private static async Task<(ScriptedResourceProbe Probe, ResourceUsage Usage)> SampleWithAsync(string?[] gpu, bool readGpu, string? pids = ScriptedResourceProbe.DefaultPids)
    {
        using CancellationTokenSource window = new();
        ScriptedResourceProbe probe = new(["1MiB"], gpu, window, null, pids);
        ResourceSampler sampler = new(probe, "engine-box", new ManualTimeProvider(), readGpu);
        Task run = sampler.RunAsync(window.Token);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        return (probe, sampler.Usage);
    }

    [Fact]
    public async Task TheCpuTierNeverAsksTheGpu()
    {
        (ScriptedResourceProbe probe, _) = await SampleWithAsync(["101, 800"], readGpu: false);
        Assert.Equal(0, probe.GpuReads);
    }

    [Fact]
    public async Task TheCpuTierReportsVramUnknown()
    {
        (_, ResourceUsage usage) = await SampleWithAsync(["101, 800"], readGpu: false);
        Assert.Null(usage.PeakVramMb);
    }

    [Fact]
    public async Task AnUnreadablePidListSkipsTheGpuRead()
    {
        (ScriptedResourceProbe probe, _) = await SampleWithAsync(["101, 800"], readGpu: true, pids: null);
        Assert.Equal(0, probe.GpuReads);
    }

    [Fact]
    public async Task AContainerWithNoGpuProcessReportsZero()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["1MiB", "1MiB"], [ScriptedResourceProbe.ForeignGpuLine, ScriptedResourceProbe.ForeignGpuLine]);
        Assert.Equal(0, usage.PeakVramMb);
    }

    [Fact]
    public async Task AnUnreadablePidListLeavesVramUnknown()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["1MiB", "1MiB"], ["101, 800", "101, 800"], pids: "PID\nnope\n");
        Assert.Null(usage.PeakVramMb);
    }

    [Fact]
    public async Task AnUnreadableGpuLineSkipsThatSampleOnly()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(["1MiB", "1MiB", "1MiB"], ["101, 800", "101, [N/A]", "101, 700"]);
        Assert.Equal(800, usage.PeakVramMb);
    }

    [Fact]
    public async Task AProbeTimeoutIsASkippedSampleNotTheEndOfTheWindow()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(
            ["1MiB", "1MiB", "1MiB"], ["101, 800", "101, 900", "101, 700"], wrap: inner => new ThrowingResourceProbe(inner, gpuRead: 1, failure: new OperationCanceledException()));
        Assert.Equal(800, usage.PeakVramMb);
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
    public async Task AThrowingGpuReadIsSkippedAndTheRunKeepsGoing()
    {
        ResourceUsage usage = await ScriptedResourceProbe.SampleAsync(
            ["1MiB", "1MiB", "1MiB"], ["101, 800", "101, 3600", "101, 700"], wrap: inner => new ThrowingResourceProbe(inner, gpuRead: 1));
        Assert.Equal(800, usage.PeakVramMb);
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
