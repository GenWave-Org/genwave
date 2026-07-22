namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="IRandomSource"/> double (STORY-213): returns each of
/// <paramref name="values"/> in turn, then repeats the last value forever. Gives single-pick specs
/// exact control over <see cref="PersonaRanker"/>'s two draws per pick (the exploration roll, then the
/// softmax sample) without needing a real distribution over many iterations.
/// </summary>
public sealed class StubRandomSource(params double[] values) : IRandomSource
{
    int index;

    public double NextDouble()
    {
        if (values.Length == 0)
            throw new InvalidOperationException("StubRandomSource requires at least one value.");

        var value = values[Math.Min(index, values.Length - 1)];
        index++;
        return value;
    }
}
