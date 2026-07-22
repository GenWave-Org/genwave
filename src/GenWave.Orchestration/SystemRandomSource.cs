namespace GenWave.Orchestration;

/// <summary>
/// The production <see cref="IRandomSource"/> binding — thread-safe and unseeded
/// (<see cref="Random.Shared"/>), the same choice <c>GenWave.Tts.LlmPromptBuilder</c> makes for its
/// own quirk-sampling. No consumer wires this into DI yet (PLAN T64 registers
/// <see cref="PersonaRanker"/> and this binding together); it ships now so the ranker has a real
/// implementation to run against once T64 lands, not just the test double.
/// </summary>
public sealed class SystemRandomSource : IRandomSource
{
    /// <summary>Shared instance for non-DI construction (tests, and the future DI registration).</summary>
    public static readonly SystemRandomSource Instance = new();

    public double NextDouble() => Random.Shared.NextDouble();
}
