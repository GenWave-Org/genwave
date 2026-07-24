using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Recording <see cref="IStationEventSink"/> double (SPEC F87.6, STORY-227, PLAN T90) — collects
/// every published event, in publish order, so a spec can assert exactly how many of a given kind
/// fired for one pick. Mirrors <c>GenWave.Host.Tests.CapturingEventSink</c>'s own shape one project
/// over; lives here instead because <see cref="RequestFulfillmentProvider"/>'s own specs (this
/// project) are the first in <c>GenWave.Orchestration.Tests</c> to need one.
/// </summary>
sealed class CapturingStationEventSink : IStationEventSink
{
    public List<StationEvent> Events { get; } = [];

    public void Publish(StationEvent evt) => Events.Add(evt);
}
