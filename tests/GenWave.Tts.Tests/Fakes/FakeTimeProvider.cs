namespace GenWave.Tts.Tests.Fakes;

/// <summary>
/// Controllable <see cref="TimeProvider"/> double (STORY-188) — starts at <paramref name="start"/>
/// and only moves when <see cref="Advance"/> is called, so cooldown-gated specs
/// (<see cref="DegradationController"/>) never depend on real wall-clock timing.
///
/// <see cref="LocalTimeZone"/> defaults to UTC rather than the base class's real
/// <see cref="TimeZoneInfo.Local"/> (STORY-193, F71.8) — a clock-formatting spec asserting on the
/// station-local text <see cref="GenWave.Tts.LlmCopyWriter"/> builds must not depend on whatever
/// timezone happens to be configured on the machine/container running the test; pass
/// <paramref name="localTimeZone"/> explicitly for a spec that cares about a specific offset.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset start, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => localTimeZone ?? TimeZoneInfo.Utc;

    public void Advance(TimeSpan delta) => now += delta;
}
