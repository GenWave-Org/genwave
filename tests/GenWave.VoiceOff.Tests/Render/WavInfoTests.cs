using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class WavInfoTests
{
    [Fact]
    public void OneSecondOfAudioIs1000Ms() =>
        Assert.Equal(1000, WavInfo.Read(WavBytes.Make(24000, 48000), out _)?.DurationMs);

    [Fact]
    public void AChunkBeforeFmtIsSkipped() =>
        Assert.Equal(500, WavInfo.Read(WavBytes.Make(24000, 24000, extraChunk: "LIST"), out _)?.DurationMs);

    [Fact]
    public void AStreamingPlaceholderSizeMeansTheDataRunsToTheEnd() =>
        Assert.Equal(1000, WavInfo.Read(WavBytes.Make(24000, 48000, declaredData: uint.MaxValue), out _)?.DurationMs);

    [Fact]
    public void NotRiffIsRefused() => Assert.Null(WavInfo.Read(WavBytes.Make(24000, 100, riff: "RIFX"), out _));

    [Fact]
    public void NotRiffSaysSo()
    {
        WavInfo.Read(WavBytes.Make(24000, 100, riff: "RIFX"), out string? problem);
        Assert.Contains("RIFF", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ARiffThatIsNotWaveIsRefused() => Assert.Null(WavInfo.Read(WavBytes.Make(24000, 100, form: "AVI "), out _));

    [Fact]
    public void AFewBytesAreNotAWav() => Assert.Null(WavInfo.Read([1, 2, 3], out _));

    [Fact]
    public void ADataChunkThatPromisesMoreThanArrivedIsRefused() =>
        Assert.Null(WavInfo.Read(WavBytes.Make(24000, 100, declaredData: 48000), out _));

    [Fact]
    public void ATruncatedDataChunkSaysTruncated()
    {
        WavInfo.Read(WavBytes.Make(24000, 100, declaredData: 48000), out string? problem);
        Assert.Contains("truncated", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFmtChunkIsRefused() => Assert.Null(WavInfo.Read(WavBytes.Make(24000, 100, withFmt: false), out _));

    [Fact]
    public void AFmtChunkTooShortForAByteRateIsRefused() => Assert.Null(WavInfo.Read(WavBytes.Make(24000, 100, fmtSize: 8), out _));

    [Fact]
    public void NoDataChunkIsRefused() =>
        Assert.Null(WavInfo.Read(WavBytes.Truncate(WavBytes.Make(24000, 0), 36), out _));
}
