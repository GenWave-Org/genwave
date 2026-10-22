namespace GenWave.VoiceOff.Tests.Support;

/// <summary>A clock that always reads one instant; timers and timestamps stay real, so deadlines and wall times still work.</summary>
internal sealed class FixedClockTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
