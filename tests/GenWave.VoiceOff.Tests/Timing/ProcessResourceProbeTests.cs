using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class ProcessResourceProbeTests
{
    private const string NoSuchProgram = "genwave-no-such-program-t610";

    [Fact]
    public async Task AMissingGpuToolIsNoAnswerNotAFailure() =>
        Assert.Null(await new ProcessResourceProbe(nvidiaSmiProgram: NoSuchProgram).ReadGpuProcessesAsync(CancellationToken.None));

    [Fact]
    public async Task AMissingDockerIsNoAnswerNotAFailure() =>
        Assert.Null(await new ProcessResourceProbe(dockerProgram: NoSuchProgram).ReadContainerMemoryAsync("engine-box", CancellationToken.None));

    [Fact]
    public async Task AMissingDockerGivesNoPids() =>
        Assert.Null(await new ProcessResourceProbe(dockerProgram: NoSuchProgram).ReadContainerPidsAsync("engine-box", CancellationToken.None));

    [Fact]
    public async Task AToolThatExitsNonZeroIsNoAnswer() =>
        Assert.Null(await new ProcessResourceProbe(nvidiaSmiProgram: "false").ReadGpuProcessesAsync(CancellationToken.None));

    [Fact]
    public async Task AToolThatExitsZeroGivesItsOutput() =>
        Assert.Equal("--query-compute-apps=pid,used_memory --format=csv,noheader,nounits\n",
            await new ProcessResourceProbe(nvidiaSmiProgram: "echo").ReadGpuProcessesAsync(CancellationToken.None));

    [Fact]
    public void TheTopArgvIsReadOnlyWithTheNameAfterTheDoubleDash() =>
        Assert.Equal(["top", "--", "genwave-kokoro-1", "-eo", "pid"], ProcessResourceProbe.TopArguments("genwave-kokoro-1"));
}
