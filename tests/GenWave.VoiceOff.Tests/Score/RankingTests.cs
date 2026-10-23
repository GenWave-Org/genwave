using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Score;

public sealed class RankingTests
{
    private const string Baseline = VoteMatrix.Baseline;

    private static string[] Order(Matrix matrix, params string[] engines) => [.. Ranking.Order(matrix, engines).Select(c => c.Engine)];

    [Fact]
    public void BeatingTheMarginOutranksAHigherRateThatDoesNot() =>
        // aaa: 5 of 5 is 1.00 but only 5 decided pairs. bbb: 30 of 40 is 0.75 and beats the margin.
        Assert.Equal(["bbb", "aaa"], Order(VoteMatrix.Of(("aaa", 5, 0), ("bbb", 30, 10)), "aaa", "bbb"));

    [Fact]
    public void WinRateOrdersTheRest() =>
        Assert.Equal(["bbb", "ccc", "aaa"], Order(VoteMatrix.Of(("aaa", 2, 8), ("bbb", 9, 1), ("ccc", 6, 4)), "aaa", "bbb", "ccc"));

    [Fact]
    public void MoreDecidedPairsBreakARateTie() =>
        Assert.Equal(["bbb", "aaa"], Order(VoteMatrix.Of(("aaa", 5, 5), ("bbb", 6, 6)), "aaa", "bbb"));

    [Fact]
    public void SlugBreaksARateAndCountTie() =>
        Assert.Equal(["aaa", "bbb"], Order(VoteMatrix.Of(("bbb", 5, 5), ("aaa", 5, 5)), "bbb", "aaa"));

    [Fact]
    public void AnEngineLeftOutOfTheListIsNotRanked() =>
        Assert.Equal(["aaa"], Order(VoteMatrix.Of(("aaa", 5, 5), ("zzz", 10, 0)), "aaa"));

    [Fact]
    public void AnEngineWithNoPairsStillRanksLast() =>
        Assert.Equal(["bbb", "aaa"], Order(VoteMatrix.Of(("bbb", 1, 9)), "aaa", "bbb"));

    [Fact]
    public void TheWinnerOfTheVerdictIsTheFirstRankedEngineThatBeats()
    {
        Matrix matrix = VoteMatrix.Of(("aaa", 5, 0), ("bbb", 30, 10));
        Assert.Equal("BEATS QUILLBASE ON PAPER, WINCES: not counted", Verdict.Decide(matrix, Baseline, new Dictionary<string, int>()).Text);
    }
}
