namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// The LLM's usable context window and what it means for DJ memory (SPEC F218.21, STORY-539).
/// A synchronous in-memory read — implementations never call the network here; the window is
/// refreshed on the dependency-health cadence (<see cref="LlmContextWindowProbe"/>).
/// </summary>
public interface ILlmContextWindow
{
    /// <summary>The window in tokens, or null when unknown (or the LLM is disabled).</summary>
    int? Tokens { get; }

    /// <summary>Memory on only when <see cref="Tokens"/> is known and at least <see cref="MemoryBudget.MinContextTokens"/>.</summary>
    MemoryBudget Budget { get; }

    /// <summary>
    /// The LLM tile's red text — "context window N &lt; 8192, memory off" or "context window unknown,
    /// memory off" — or null when memory is on or the LLM is disabled (nothing to be red about).
    /// </summary>
    string? MemoryOffReason { get; }
}
