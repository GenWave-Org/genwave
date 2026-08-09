namespace GenWave.Context.Tests.Fakes;

using GenWave.Core.Abstractions;

/// <summary>
/// Fixed <see cref="IStationClockProvider"/> double (mirrors
/// <c>GenWave.Tts.Tests.Fakes.FakeStationClockProvider</c> one project over) — hands back exactly the
/// station-local instant a fact pins, so a provider that reads this seam can be proven to use IT
/// rather than <see cref="TimeProvider"/>'s own zone.
/// </summary>
sealed class FakeStationClockProvider(DateTimeOffset localNow, TimeZoneInfo? zone = null) : IStationClockProvider
{
    public DateTimeOffset LocalNow => localNow;

    public TimeZoneInfo Zone => zone ?? TimeZoneInfo.Utc;
}
