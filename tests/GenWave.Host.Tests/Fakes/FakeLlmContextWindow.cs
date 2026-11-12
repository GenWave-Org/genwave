using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Tests.Fakes;

/// <summary>A fixed <see cref="ILlmContextWindow"/> answer for status-endpoint specs.</summary>
public sealed class FakeLlmContextWindow(int? tokens = null, string? memoryOffReason = null) : ILlmContextWindow
{
    public int? Tokens { get; } = tokens;

    public MemoryBudget Budget => MemoryBudget.ForContextWindow(Tokens);

    public string? MemoryOffReason { get; } = memoryOffReason;
}
