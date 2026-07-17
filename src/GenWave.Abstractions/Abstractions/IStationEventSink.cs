using GenWave.Core.Events;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The in-process domain-event seam (gitea-#246, gap H2). Publishers at the five choke points (feeder
/// advance, enricher flip, TTS render, settings write, admin writes) call
/// <see cref="Publish"/> fire-and-forget; the default binding is
/// <see cref="Events.NoOpStationEventSink"/>, and an analytics/audit module subscribes by
/// replacing or decorating the binding. No bus, no outbox, no persistence.
/// </summary>
public interface IStationEventSink
{
    /// <summary>
    /// Publishes one event. Implementations MUST NOT throw and MUST return promptly — publishers
    /// sit on hot paths (the feeder tick, enrichment workers); a subscriber with real work to do
    /// queues it internally and drains on its own schedule.
    /// </summary>
    void Publish(StationEvent evt);
}
