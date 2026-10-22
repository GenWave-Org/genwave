using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class DockerStatsParserTests
{
    [Theory]
    [InlineData("2.884GiB / 4GiB", 2953.216)]
    [InlineData("512MiB / 4GiB", 512)]
    [InlineData("1048576B / 4GiB", 1)]
    [InlineData("1024KiB / 4GiB", 1)]
    [InlineData("1MiB / 4GiB", 1)]
    [InlineData("1GiB / 4GiB", 1024)]
    [InlineData("1TiB / 4GiB", 1048576)]
    [InlineData("1048.576kB / 4GiB", 1)]
    [InlineData("1.048576MB / 4GiB", 1)]
    [InlineData("1.048576GB / 4GiB", 1000)]
    [InlineData("1TB / 4GiB", 953674.31640625)]
    [InlineData("2.884GiB / 4GiB\n", 2953.216)]
    public void ALeftSideUsageIsReadInMiB(string line, double mib) =>
        Assert.Equal(mib, DockerStatsParser.ParseMib(line) ?? double.NaN, 6);

    [Theory]
    [InlineData("-- / --")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("12 parsecs / 4GiB")]
    [InlineData("x512MiB / 4GiB")]
    [InlineData("Error response from daemon: No such container")]
    public void ALineThatIsNotAUsageReadingIsNotASample(string? line) =>
        Assert.Null(DockerStatsParser.ParseMib(line));

    [Fact]
    public void OnlyTheFirstLineIsRead() => Assert.Equal(1, DockerStatsParser.ParseMib("1MiB / 4GiB\n2MiB / 4GiB"));

    [Fact]
    public void OnePointTwoGiBRoundsToTheNearestWholeMb() =>
        Assert.Equal(1229, Megabytes.FromMib(DockerStatsParser.ParseMib("1.2GiB / 4GiB") ?? double.NaN));

    [Fact]
    public void ARoundHalfGoesUp() => Assert.Equal(3, Megabytes.FromMib(2.5));
}
