namespace GenWave.Core.Domain;

/// <summary>
/// The F71.3 sampling rule, shared by every quirk picker so it cannot drift: 2-3 items per
/// generation, never the whole set once there are more than three.
/// </summary>
public static class QuirkSampling
{
    const int MinSampled = 2;
    const int MaxSampled = 3;

    /// <summary>
    /// Samples 2-3 of <paramref name="items"/> (all of them when there are at most three), returned in
    /// their original order so the SET is random but the ORDER within one prompt is deterministic.
    /// </summary>
    public static IReadOnlyList<T> Sample<T>(IReadOnlyList<T> items, Random random)
    {
        if (items.Count <= MaxSampled)
            return items;

        var sampleSize = random.Next(MinSampled, MaxSampled + 1);
        var indices = Enumerable.Range(0, items.Count).ToArray();
        random.Shuffle(indices);
        return indices.Take(sampleSize).Order().Select(i => items[i]).ToList();
    }
}
