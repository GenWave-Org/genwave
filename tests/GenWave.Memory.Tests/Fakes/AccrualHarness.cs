using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// The accrual consumer wired to fakes: a station clock that starts Tuesday 2026-11-10 06:05 station-local (12:05 UTC), a hand-set
/// on-air snapshot, and an in-memory episode store. Tests feed it events and then <see cref="FlushAsync"/> what it queued.
/// </summary>
public sealed class AccrualHarness
{
    public static readonly DateTimeOffset TuesdayAt0605 = new(2026, 11, 10, 12, 5, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; } = new(TuesdayAt0605);

    public FakeOnAir OnAir { get; } = new();

    public InMemoryEpisodeStore Store { get; }

    public EpisodeAccrualSink Sink { get; }

    public AccrualHarness()
    {
        Store = new InMemoryEpisodeStore(Time);
        Sink = new EpisodeAccrualSink(Store, OnAir, new FakeStationClock(Time));
    }

    /// <summary>A music track starting on air.</summary>
    public void MusicAired(string title, string artist) =>
        Sink.Publish(new TrackAired("media:1", title, artist, 0, Time.GetUtcNow(), 180_000));

    /// <summary>A patter segment starting on air.</summary>
    public void PatterAired(SegmentKind kind, string segmentId = "tts:1") =>
        Sink.Publish(new TrackAired(segmentId, "Station", "Persona", 0, Time.GetUtcNow(), 8_000, SegmentKind: kind));

    /// <summary>Drains everything the sink has queued into the store, as the Host's drain loop would.</summary>
    public async Task FlushAsync()
    {
        Sink.Complete();
        await Sink.RunAsync(ex => throw ex, CancellationToken.None);
    }
}
