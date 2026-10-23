namespace GenWave.VoiceOff.Pairing;

/// <summary>
/// Turns the planned pairs into listening sessions of at most <see cref="Cap"/>. The pairs are laid out engine by engine (an engine's lanes
/// side by side) and dealt round-robin across n = ceil(count / Cap) sessions with one running counter, so each engine's pairs land on
/// consecutive sessions; each session is then shuffled with the seeded rng. Dealing straight from an interleaved sequence would alias
/// with the group count and could hand a session a single engine. Guarantee: an engine with at least n pairs appears in every session,
/// so every session holds more than one engine as long as at least two engines have n or more pairs each.
/// With exactly one challenger that cannot hold, because only one engine is ever paired.
/// </summary>
internal static class SessionCutter
{
    internal const int Cap = 40;

    internal static IReadOnlyList<IReadOnlyList<PlannedPair>> Cut(IReadOnlyList<PlannedPair> pairs, Random rng)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(rng);

        int sessions = (pairs.Count + Cap - 1) / Cap;
        List<List<PlannedPair>> dealt = [.. Enumerable.Range(0, sessions).Select(_ => new List<PlannedPair>())];
        IEnumerable<PlannedPair> ordered = pairs
            .OrderBy(p => p.Challenger, StringComparer.Ordinal)
            .ThenBy(p => p.Lane);
        int k = 0;
        foreach (PlannedPair pair in ordered)
        {
            dealt[k % sessions].Add(pair);
            k++;
        }

        foreach (List<PlannedPair> session in dealt)
        {
            Shuffle(session, rng);
        }

        return [.. dealt];
    }

    private static void Shuffle(List<PlannedPair> items, Random rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
