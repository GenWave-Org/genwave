using GenWave.Core.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>The station clock over a <see cref="FakeTimeProvider"/>, in a fixed UTC-6 zone (Alberta).</summary>
public sealed class FakeStationClock : IStationClockProvider
{
    readonly FakeTimeProvider time;

    public FakeStationClock(FakeTimeProvider time)
    {
        this.time = time;
        Zone = TimeZoneInfo.CreateCustomTimeZone("Station", TimeSpan.FromHours(-6), "Station", "Station");
        time.SetLocalTimeZone(Zone);
    }

    public DateTimeOffset LocalNow => time.GetLocalNow();

    public TimeZoneInfo Zone { get; }
}
