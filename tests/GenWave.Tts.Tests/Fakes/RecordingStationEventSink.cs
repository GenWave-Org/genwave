namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Events;

/// <summary>Keeps every <see cref="StationEvent"/> published to it.</summary>
public sealed class RecordingStationEventSink : IStationEventSink
{
    public List<StationEvent> Events { get; } = [];

    public void Publish(StationEvent evt) => Events.Add(evt);
}
