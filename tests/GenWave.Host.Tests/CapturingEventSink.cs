using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Tests;

/// <summary>
/// Test double for <see cref="IStationEventSink"/> (gitea-#246): records every published event and
/// optionally forwards <see cref="TrackAired"/> to <see cref="OnTrackAired"/> — the settable
/// hook specs use where they previously assigned the feeder's retired <c>OnAdvance</c> callback.
/// </summary>
sealed class CapturingEventSink : IStationEventSink
{
    public List<StationEvent> Events { get; } = [];

    /// <summary>Set AFTER feeder construction, exactly where <c>OnAdvance</c> used to be assigned.</summary>
    public Action<TrackAired>? OnTrackAired { get; set; }

    public void Publish(StationEvent evt)
    {
        Events.Add(evt);
        if (evt is TrackAired t)
            OnTrackAired?.Invoke(t);
    }
}
