using GenWave.VoiceOff.Reeling;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Reeling;

public sealed class WinceSetTests : IDisposable
{
    private const string Baseline = VoteMatrix.Baseline;
    private readonly EngineDirectoryFixture directories = new();

    private EngineProfileSet Profiles(params string[] challengers) => EngineProfileSet.Load(directories.Create(
        [(Baseline + ".json", EngineProfileJson.Valid(Baseline).ToJsonString()),
         .. challengers.Select(slug => (slug + ".json", EngineProfileJson.Valid(slug, baseline: null).ToJsonString()))]));

    private static GateEntry Gate(string slug, bool passed) =>
        new(slug, "MIT", true, null, passed ? GateResult.Pass("ok") : GateResult.Fail("no"), GateResult.Pass("ok"));

    [Fact]
    public void TheBaselineIsGatedButNeverChosenAsTheRunnerUp()
    {
        // With one gated challenger the baseline does not step up to be the runner-up: the set is the winner and the baseline reel, and it is short.
        WinceChoice choice = WinceSet.Choose(
            Profiles("aaa"),
            [Gate(Baseline, true), Gate("aaa", true)],
            VoteMatrix.Of(("aaa", 3, 7)));
        Assert.Equal($"aaa,{Baseline} short={true}", $"{string.Join(',', choice.Engines)} short={choice.IsShort}");
    }

    [Fact]
    public void AChallengerThatFailedAGateIsExcludedEvenWithTheBestRate()
    {
        WinceChoice choice = WinceSet.Choose(
            Profiles("aaa", "bbb", "ccc"),
            [Gate(Baseline, true), Gate("aaa", true), Gate("bbb", false), Gate("ccc", true)],
            VoteMatrix.Of(("aaa", 2, 8), ("bbb", 10, 0), ("ccc", 5, 5)));
        Assert.Equal(["ccc", "aaa", Baseline], choice.Engines);
    }

    public void Dispose() => directories.Dispose();
}
