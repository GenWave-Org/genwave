using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Serving;

namespace GenWave.VoiceOff.Tests.Serving;

public sealed class ResumeOrderTests
{
    private static readonly IReadOnlyList<SessionPair> Shuffled = [.. new[] { "e", "d", "c", "b", "a" }.Select(id => new SessionPair(id, id + "A", id + "B"))];

    private static string[] Ids(IReadOnlySet<string> voted) => [.. ResumeOrder.For(Shuffled, voted).Select(p => p.PairId)];

    [Fact]
    public void VotedPairsComeFirstAndBothGroupsKeepTheirOrder() =>
        Assert.Equal(["d", "b", "e", "c", "a"], Ids(new HashSet<string> { "b", "d" }));

    [Fact]
    public void NoVotesKeepsTheShuffle() =>
        Assert.Equal(["e", "d", "c", "b", "a"], Ids(new HashSet<string>()));

    [Fact]
    public void AllVotedKeepsTheShuffle() =>
        Assert.Equal(["e", "d", "c", "b", "a"], Ids(new HashSet<string> { "a", "b", "c", "d", "e" }));

    [Fact]
    public void VotesForPairsOutsideTheSessionAreIgnored() =>
        Assert.Equal(["c", "e", "d", "b", "a"], Ids(new HashSet<string> { "c", "zzz" }));
}
