using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Serving;

namespace GenWave.VoiceOff.Tests.Score;

public sealed class RankingTests
{
    private static readonly RunManifest Manifest = new(
        "r",
        "hash",
        DateTimeOffset.UnixEpoch,
        new Dictionary<string, CardEntry>
        {
            ["a"] = new("thor-cpu", true),
            ["b"] = new("thor-cpu", true),
            ["c"] = new("thor-cpu", false, "down"),
        });

    [Fact]
    public void AnIdTheRunLacksOrDidNotRenderOrRepeatsIsSkipped()
    {
        Judgement judgement = new(DateTimeOffset.UnixEpoch, ["b", "zzz", "c", "b", "a"], new Dictionary<string, CardJudgement>());

        Assert.Equal(["b", "a"], Ranking.Of(Manifest, judgement).Select(r => r.CardId));
    }

    [Fact]
    public void RanksCountFromOne() =>
        Assert.Equal([1, 2], Ranking.Of(Manifest, new Judgement(DateTimeOffset.UnixEpoch, [], new Dictionary<string, CardJudgement>())).Select(r => r.Rank));
}
