using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="IEnvelopeProvider"/> double (SPEC F81.1/F81.3, mirrors
/// <see cref="FakeBoundaryBiasProvider"/> one seam over). Set <see cref="Envelope"/> between calls
/// to simulate a live <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload without standing up a real
/// options stack in a unit test.
/// </summary>
sealed class FakeEnvelopeProvider(SegmentEnvelope envelope) : IEnvelopeProvider
{
    public SegmentEnvelope Envelope { get; set; } = envelope;

    public SegmentEnvelope Current => Envelope;
}
