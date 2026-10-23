using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class NvidiaSmiParserTests
{
    private static readonly IReadOnlySet<int> Mine = new HashSet<int> { 101, 102 };

    [Fact]
    public void ThePidsAreReadUnderTheHeader() =>
        Assert.Equal([101, 102], NvidiaSmiParser.ParsePids("PID\n101\n102\n")?.Order());

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("PID\n")]
    [InlineData("PID\n101\nnope\n")]
    public void NoUsablePidsMeansUnknown(string? output) => Assert.Null(NvidiaSmiParser.ParsePids(output));

    [Fact]
    public void OnlyTheContainersProcessesAreSummed() =>
        Assert.Equal(3600, NvidiaSmiParser.ParseContainerUsedMib("101, 1800\n999, 9000\n102, 1800\n", Mine));

    [Fact]
    public void NoProcessOfTheContainerOnTheGpuIsZero() =>
        Assert.Equal(0, NvidiaSmiParser.ParseContainerUsedMib("999, 9000\n", Mine));

    [Fact]
    public void AnEmptyAnswerFromAWorkingToolIsZero() =>
        Assert.Equal(0, NvidiaSmiParser.ParseContainerUsedMib("", Mine));

    [Theory]
    [InlineData(null)]
    [InlineData("NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver.")]
    [InlineData("101, [N/A]\n")]
    [InlineData("101, 800\n[N/A]\n")]
    [InlineData("101\n")]
    public void ADriverErrorOrAnUnreadableLineMeansUnknown(string? output) =>
        Assert.Null(NvidiaSmiParser.ParseContainerUsedMib(output, Mine));
}
