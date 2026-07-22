namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.3 — <see cref="PersonaRanker"/>'s tunables, defaulted to the PRD's proposed values
/// pending a listening pass against the demo library (TODO noted in SPEC.md, not this task's to
/// resolve). This record only ever carries the raw operator-facing setting: <see cref="PersonaRanker"/>
/// itself — not this record — is where F82.4's hard 5% exploration floor is enforced
/// (<see cref="PersonaRanker.MinimumExplorationRate"/>), so an operator setting of exactly 0 here is
/// preserved as written rather than silently rewritten to 0.05 at construction.
/// </summary>
public sealed record PersonaRankerOptions
{
    /// <summary>Multiplier on a matched taste rule's weight in the score sum (SPEC F82.2).</summary>
    public double BiasGain { get; init; } = 1.0;

    /// <summary>Multiplier on the absolute energy-vs-target distance penalty (SPEC F82.2).</summary>
    public double EnergyPull { get; init; } = 2.0;

    /// <summary>Softmax temperature over the Top-K scored pool (SPEC F82.3).</summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>How many top-scored candidates enter the softmax sample (SPEC F82.3).</summary>
    public int TopK { get; init; } = 18;

    /// <summary>
    /// The operator-facing exploration-slice setting (SPEC F82.3, F82.4). <see cref="PersonaRanker"/>
    /// clamps this up to its own 5% floor at pick time — this property itself is never clamped.
    /// </summary>
    public double ExplorationRate { get; init; } = 0.15;
}
