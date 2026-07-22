namespace GenWave.Orchestration;

/// <summary>
/// Seedable randomness seam for <see cref="PersonaRanker"/>'s exploration-slice roll and softmax
/// sample draw (SPEC F82.3, F82.4) — an interface rather than mirroring <see cref="TimeProvider"/>'s
/// abstract-class shape, since there is exactly one member and no shared cross-cutting behavior
/// (time zone conversion, timers) to centralize on a base type. Mirrors the same DI-seam idea
/// <see cref="TimeProvider"/> already establishes in this project (<c>SpeechDeferralQueue</c>,
/// <c>Orchestrator</c>): production binds an unseeded generator (<see cref="SystemRandomSource"/>);
/// distribution specs inject a seeded implementation so thousands of in-memory picks are
/// reproducible without any wall-clock or I/O dependency.
/// </summary>
public interface IRandomSource
{
    /// <summary>A value in <c>[0, 1)</c>, matching <see cref="Random.NextDouble"/>'s own contract.</summary>
    double NextDouble();
}
