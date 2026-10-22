using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class ProcessResourceProbeTests
{
    private const string NoSuchProgram = "genwave-no-such-program-t610";

    [Fact]
    public async Task AMissingGpuToolIsNoAnswerNotAFailure() =>
        Assert.Null(await new ProcessResourceProbe(nvidiaSmiProgram: NoSuchProgram).ReadGpuMemoryAsync(CancellationToken.None));

    [Fact]
    public async Task AMissingDockerIsNoAnswerNotAFailure() =>
        Assert.Null(await new ProcessResourceProbe(dockerProgram: NoSuchProgram).ReadContainerMemoryAsync("engine-box", CancellationToken.None));

    [Fact]
    public async Task AToolThatExitsNonZeroIsNoAnswer() =>
        Assert.Null(await new ProcessResourceProbe(nvidiaSmiProgram: "false").ReadGpuMemoryAsync(CancellationToken.None));

    [Fact]
    public async Task AToolThatExitsZeroGivesItsOutput() =>
        Assert.Equal("--query-gpu=memory.used --format=csv,noheader,nounits\n",
            await new ProcessResourceProbe(nvidiaSmiProgram: "echo").ReadGpuMemoryAsync(CancellationToken.None));
}
