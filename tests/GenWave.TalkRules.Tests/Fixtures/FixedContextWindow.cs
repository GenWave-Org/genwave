using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A context window with a fixed memory budget.</summary>
internal sealed class FixedContextWindow(MemoryBudget budget) : ILlmContextWindow
{
    public int? Tokens => budget.MemoryOn ? MemoryBudget.MinContextTokens : null;

    public MemoryBudget Budget => budget;

    public string? MemoryOffReason => null;
}
