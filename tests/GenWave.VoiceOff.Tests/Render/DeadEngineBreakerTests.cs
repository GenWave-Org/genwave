using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class DeadEngineBreakerTests
{
    private static ClipOutcome Failure(SpeechErrorKind kind) => ClipOutcome.Failed("no", 0, kind);

    private static ClipOutcome Success() => new(true, null, null, 1, 1, true);

    private static DeadEngineBreaker After(params ClipOutcome[] outcomes)
    {
        DeadEngineBreaker breaker = new();
        foreach (ClipOutcome outcome in outcomes)
        {
            breaker.Record(outcome);
        }

        return breaker;
    }

    [Fact]
    public void ThreeUnreachableInARowTrips() =>
        Assert.True(After(Failure(SpeechErrorKind.Unreachable), Failure(SpeechErrorKind.Timeout), Failure(SpeechErrorKind.Unreachable)).Tripped);

    [Fact]
    public void TwoDoNot() =>
        Assert.False(After(Failure(SpeechErrorKind.Unreachable), Failure(SpeechErrorKind.Unreachable)).Tripped);

    [Fact]
    public void ASuccessResetsTheCount() =>
        Assert.False(After(Failure(SpeechErrorKind.Unreachable), Failure(SpeechErrorKind.Unreachable), Success(), Failure(SpeechErrorKind.Unreachable)).Tripped);

    [Fact]
    public void AnEngineThatAnswersWithAnErrorIsNotDead() =>
        Assert.False(After(Failure(SpeechErrorKind.HttpStatus), Failure(SpeechErrorKind.HttpStatus), Failure(SpeechErrorKind.HttpStatus)).Tripped);

    [Fact]
    public void AFailureWithNoEngineErrorIsNotAStrike() =>
        Assert.False(After(ClipOutcome.Failed("match", 0), ClipOutcome.Failed("match", 0), ClipOutcome.Failed("match", 0)).Tripped);
}
