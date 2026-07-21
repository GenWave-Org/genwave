using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Playout;

/// <summary>
/// Fans <see cref="Publish"/> out to every sink in <paramref name="sinks"/> (SPEC F72.1, STORY-195;
/// gitea-#246's own "decorating the binding" language) — the host's ONE bound
/// <see cref="IStationEventSink"/> composes however many consumers exist today
/// (<see cref="PlayHistoryEventSink"/>, the booth log's <c>BoothLogWriter</c>) rather than each
/// consumer wrapping the next. A future consumer is added by appending to the list
/// <see cref="PlayoutServiceCollectionExtensions"/> builds this from, never by re-wiring an existing
/// sink's constructor.
///
/// Each child call is isolated in its own try/catch — a WARN, not a rethrow — so one misbehaving
/// sink can never stop the others (or the publisher), even though every sink here already promises
/// (per <see cref="IStationEventSink"/>'s own contract) never to throw on its own.
/// </summary>
sealed class CompositeStationEventSink(IReadOnlyList<IStationEventSink> sinks, ILogger<CompositeStationEventSink> logger)
    : IStationEventSink
{
    public void Publish(StationEvent evt)
    {
        foreach (var sink in sinks)
        {
            try
            {
                sink.Publish(evt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Station event sink {Sink} threw on {Event}",
                    sink.GetType().Name, evt.GetType().Name);
            }
        }
    }
}
