using GenWave.Core.Abstractions;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>An <see cref="ILlmBatchGate"/> that allows the batch unless built to decline.</summary>
public sealed class FakeLlmBatchGate(bool allowed = true) : ILlmBatchGate
{
    public LlmBatchGateDecision Evaluate() => new(allowed, allowed ? "test: allowed" : "test: declined");
}
