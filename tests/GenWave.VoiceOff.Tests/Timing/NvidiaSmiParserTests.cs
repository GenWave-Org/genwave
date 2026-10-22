using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class NvidiaSmiParserTests
{
    [Fact]
    public void OneGpuLineIsItsMiB() => Assert.Equal(2277, NvidiaSmiParser.ParseUsedMib("2277\n"));

    [Fact]
    public void SeveralGpuLinesAreSummed() => Assert.Equal(3000, NvidiaSmiParser.ParseUsedMib("2277\n723\n"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver.")]
    [InlineData("2277\n[N/A]\n")]
    public void NoNumbersMeansUnknown(string? output) => Assert.Null(NvidiaSmiParser.ParseUsedMib(output));

    [Fact]
    public void ADeltaBelowTheBaselineIsZero() => Assert.Equal(0, NvidiaSmiParser.DeltaMb(800, 500));
}
