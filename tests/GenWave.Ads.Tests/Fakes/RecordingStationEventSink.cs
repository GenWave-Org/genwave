namespace GenWave.Ads.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Events;

/// <summary>Keeps every published station event, in order.</summary>
public sealed class RecordingStationEventSink : IStationEventSink
{
    readonly List<StationEvent> published = [];

    public IReadOnlyList<StationEvent> Published => published;

    public void Publish(StationEvent stationEvent) => published.Add(stationEvent);
}
