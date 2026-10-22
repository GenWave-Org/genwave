using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class RtfSummaryTests
{
    private static IEnumerable<TimingRecord> Rtfs(params double[] values) =>
        values.Select(v => TimingRecordTests.Record(wallMs: v * 1000, audioMs: 1000));

    private static RtfSummary Of(params double[] values) =>
        RtfSummary.From(Rtfs(values)) ?? throw new InvalidOperationException("expected a summary");

    [Fact]
    public void NoRecordsHaveNoSummary() => Assert.Null(RtfSummary.From([]));

    [Fact]
    public void OneValueIsItsOwnMedian() => Assert.Equal(3, Of(3).Median);

    [Fact]
    public void OneValueIsItsOwnP95() => Assert.Equal(3, Of(3).P95);

    [Fact]
    public void TheP95OfTwoValuesInterpolatesBetweenThem() => Assert.Equal(1.95, Of(1, 2).P95, 10);

    [Fact]
    public void TheOrderRecordsArriveInDoesNotMatter() => Assert.Equal(2, Of(3, 1, 2).Median);

    [Fact]
    public void FailedRendersAreIgnored() =>
        Assert.Equal(2, RtfSummary.From([.. Rtfs(1, 3), TimingRecordTests.Record(wallMs: 900000, audioMs: 1000, ok: false)])?.Median);

    [Fact]
    public void RendersWithoutAudioAreIgnored() =>
        Assert.Equal(2, RtfSummary.From([.. Rtfs(1, 3), TimingRecordTests.Record(audioMs: 0)])?.Median);

    [Fact]
    public void OnlyFailedRendersHaveNoSummary() =>
        Assert.Null(RtfSummary.From([TimingRecordTests.Record(ok: false)]));
}
