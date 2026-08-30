using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Fixed <see cref="IStationDefaultEnvelopeSource"/> double (SPEC F153.8; STORY-378; PLAN T376) —
/// mirrors <c>GenWave.Orchestration.Tests.Fakes.FakeStationDefaultEnvelopeSource</c> one seam over,
/// for <c>UnreachableGardenerPass</c> specs that need to drive the station-default fallback (SPEC
/// F91.4) directly.
/// </summary>
public sealed class FakeStationDefaultEnvelopeSource(SegmentEnvelope envelope) : IStationDefaultEnvelopeSource
{
    public SegmentEnvelope Current => envelope;
}
