using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// Fixed <see cref="IStationDefaultEnvelopeSource"/> double (STORY-241, PLAN T119) — mirrors
/// <see cref="FakeEnvelopeProvider"/> one seam over.
/// </summary>
public sealed class FakeStationDefaultEnvelopeSource(SegmentEnvelope envelope) : IStationDefaultEnvelopeSource
{
    /// <inheritdoc/>
    public SegmentEnvelope Current => envelope;
}
