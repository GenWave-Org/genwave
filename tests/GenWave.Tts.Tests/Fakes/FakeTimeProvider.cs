namespace GenWave.Tts.Tests.Fakes;

/// <summary>
/// Controllable <see cref="TimeProvider"/> double (STORY-188) — starts at <paramref name="start"/>
/// and only moves when <see cref="Advance"/> is called, so cooldown-gated specs
/// (<see cref="DegradationController"/>) never depend on real wall-clock timing.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;
}
