using GenWave.Core.Abstractions;

namespace GenWave.Tts.Tests.Fakes;

/// <summary>
/// Fixed <see cref="IStationClockProvider"/> double (gh-#117) — hands back exactly the
/// station-local instant a spec pins, so clock-line facts assert on a known zoned wall time
/// rather than whatever timezone the machine running the test happens to be in.
/// </summary>
public sealed class FakeStationClockProvider(DateTimeOffset localNow, TimeZoneInfo? zone = null) : IStationClockProvider
{
    public DateTimeOffset LocalNow => localNow;

    public TimeZoneInfo Zone => zone ?? TimeZoneInfo.Utc;
}
