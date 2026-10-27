using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class DeadEngineBreakerTests
{
    private static CardOutcome Failure(SpeechErrorKind kind) => CardOutcome.Failed("no", kind);

    private static CardOutcome Success() => CardOutcome.Success();

    private static DeadEngineBreaker After(params CardOutcome[] outcomes)
    {
        DeadEngineBreaker breaker = new();
        foreach (CardOutcome outcome in outcomes)
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
        Assert.False(After(CardOutcome.Failed("match"), CardOutcome.Failed("match"), CardOutcome.Failed("match")).Tripped);
}
