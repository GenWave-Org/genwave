namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Controllable <see cref="TimeProvider"/> double (STORY-197) — starts at <paramref name="start"/>
/// and only moves when <see cref="Advance"/> is called, so <see cref="SpeechDeferralQueue"/>
/// specs never depend on real wall-clock timing. Mirrors <c>GenWave.Tts.Tests</c>'s own fake.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;
}
