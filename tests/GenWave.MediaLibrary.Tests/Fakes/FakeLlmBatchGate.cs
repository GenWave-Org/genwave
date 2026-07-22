using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Configurable <see cref="ILlmBatchGate"/> double (SPEC F85.3) — defaults to "allowed" so a
/// mood-tagging spec that isn't exercising the F85.3 degradation skip itself doesn't need to think
/// about it.
/// </summary>
sealed class FakeLlmBatchGate(bool allowed = true, string reason = "test: allowed") : ILlmBatchGate
{
    public LlmBatchGateDecision Evaluate() => new(allowed, reason);
}
