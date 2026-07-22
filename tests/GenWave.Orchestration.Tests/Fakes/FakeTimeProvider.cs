namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Controllable <see cref="TimeProvider"/> double (STORY-197) — starts at <paramref name="start"/>
/// and only moves when <see cref="Advance"/> is called, so <see cref="SpeechDeferralQueue"/>
/// specs never depend on real wall-clock timing. Mirrors <c>GenWave.Tts.Tests</c>'s own fake.
///
/// <see cref="LocalTimeZone"/> defaults to UTC rather than the base class's real
/// <see cref="TimeZoneInfo.Local"/> (STORY-213, PLAN T64, mirrors <c>GenWave.Tts.Tests.Fakes.FakeTimeProvider</c>'s
/// own precedent) — a <see cref="PersonaRanker"/> spec asserting on the station-local day/hour a
/// <c>TasteContext</c> gates against must not depend on whatever timezone happens to be configured
/// on the machine/container running the test; pass <paramref name="localTimeZone"/> explicitly for a
/// spec that cares about a specific offset.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset start, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => localTimeZone ?? TimeZoneInfo.Utc;

    public void Advance(TimeSpan delta) => now += delta;
}
