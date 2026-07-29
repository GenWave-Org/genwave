using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Fixed <see cref="IStationClockProvider"/> double (gh-#117) — hands back exactly the
/// station-local instant a spec pins, so <c>SegmentRequest.LocalNow</c> facts assert on a known
/// zoned wall time rather than whatever timezone the machine running the test happens to be in.
/// </summary>
public sealed class FakeStationClockProvider(DateTimeOffset localNow) : IStationClockProvider
{
    public DateTimeOffset LocalNow => localNow;
}
