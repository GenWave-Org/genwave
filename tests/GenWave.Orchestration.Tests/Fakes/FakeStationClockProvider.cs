using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Fixed <see cref="IStationClockProvider"/> double (gh-#117) — hands back exactly the
/// station-local instant a spec pins, so <c>SegmentRequest.LocalNow</c> facts assert on a known
/// zoned wall time rather than whatever timezone the machine running the test happens to be in.
/// <paramref name="zone"/> (gh-#224) defaults to UTC — pass the real station zone for a spec that
/// exercises <c>ScheduleResolver</c>'s zone-aware boundary math.
/// </summary>
public sealed class FakeStationClockProvider(DateTimeOffset localNow, TimeZoneInfo? zone = null) : IStationClockProvider
{
    public DateTimeOffset LocalNow => localNow;

    public TimeZoneInfo Zone => zone ?? TimeZoneInfo.Utc;
}
