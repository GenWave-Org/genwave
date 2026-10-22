using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Score;

public sealed class MarginRuleTests
{
    private static Cell CellOf(params (string Judge, int Wins, int Losses)[] judges) => new(
        "beta",
        RenderLane.Plain,
        new Tally(judges.Sum(j => j.Wins), judges.Sum(j => j.Losses), 0),
        judges.ToDictionary(j => j.Judge, j => new Tally(j.Wins, j.Losses, 0), StringComparer.Ordinal));

    [Fact]
    public void ExactlyPoint65Passes() =>
        Assert.Equal(MarginOutcome.Beats, MarginRule.Evaluate(CellOf(("Dean", 13, 7), ("Wife", 13, 7))).Outcome);

    [Fact]
    public void JustBelowPoint65DoesNotBeat() =>
        Assert.Equal(MarginOutcome.DoesNotBeat, MarginRule.Evaluate(CellOf(("Dean", 13, 7), ("Wife", 12, 8))).Outcome);

    [Fact]
    public void AJudgeAtPoint54BlocksTheWin() =>
        Assert.Equal(MarginOutcome.DoesNotBeat, MarginRule.Evaluate(CellOf(("Dean", 38, 2), ("Wife", 27, 23))).Outcome);

    [Fact]
    public void AJudgeAtPoint55Passes() =>
        Assert.Equal(MarginOutcome.Beats, MarginRule.Evaluate(CellOf(("Dean", 38, 2), ("Wife", 11, 9))).Outcome);

    [Fact]
    public void AJudgeWhoOnlyTiedDoesNotBlock() =>
        Assert.Equal(MarginOutcome.Beats, MarginRule.Evaluate(CellOf(("Dean", 30, 10), ("Wife", 0, 0))).Outcome);

    [Fact]
    public void Thirty9DecidedPairsIsInsufficient() =>
        Assert.Equal(new MarginResult(MarginOutcome.Insufficient, 39), MarginRule.Evaluate(CellOf(("Dean", 39, 0))));

    [Fact]
    public void FortyDecidedPairsIsEnough() =>
        Assert.True(MarginRule.Evaluate(CellOf(("Dean", 40, 0))).Beats);

    [Fact]
    public void TiesDoNotCountTowardTheForty() =>
        Assert.Equal(MarginOutcome.Insufficient, MarginRule.Evaluate(new Cell("beta", RenderLane.Plain, new Tally(30, 9, 50), new Dictionary<string, Tally>())).Outcome);
}
