namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>An <see cref="ILlmContextWindow"/> with a fixed budget: <see cref="MemoryBudget.Standard"/> is memory on, <see cref="MemoryBudget.Off"/> a window under 8192.</summary>
public sealed class FixedContextWindow(MemoryBudget budget) : ILlmContextWindow
{
    public int? Tokens => budget.MemoryOn ? MemoryBudget.MinContextTokens : null;
    public MemoryBudget Budget => budget;
    public string? MemoryOffReason => null;
}
