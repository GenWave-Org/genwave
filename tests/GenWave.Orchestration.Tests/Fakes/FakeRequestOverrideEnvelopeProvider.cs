using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Fixed-value <see cref="IRequestOverrideEnvelopeProvider"/> double (SPEC F87.6, STORY-227,
/// PLAN T90) — mirrors <see cref="FakeBoundaryBiasProvider"/> one seam over.
/// </summary>
sealed class FakeRequestOverrideEnvelopeProvider(bool overrideEnvelope) : IRequestOverrideEnvelopeProvider
{
    public bool Current { get; } = overrideEnvelope;
}
