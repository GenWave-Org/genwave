namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// <see cref="IRandomSource"/> double (STORY-213) backed by a seeded <see cref="Random"/> — a real
/// PRNG stream, reproducible run to run, for <see cref="PersonaRanker"/> distribution facts that need
/// thousands of picks to observe a proportion or a mean (SPEC F82.2, F82.4), not a single controlled
/// draw (see <see cref="StubRandomSource"/> for that).
/// </summary>
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    readonly Random random = new(seed);

    public double NextDouble() => random.NextDouble();
}
