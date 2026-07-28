using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Fixed <see cref="IStationDefaultEnvelopeSource"/> double (STORY-241, PLAN T119) — mirrors
/// <see cref="FakeEnvelopeProvider"/> one seam over.
/// </summary>
sealed class FakeStationDefaultEnvelopeSource(SegmentEnvelope envelope) : IStationDefaultEnvelopeSource
{
    public SegmentEnvelope Current => envelope;
}
