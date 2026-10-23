namespace GenWave.VoiceOff.Listening;

/// <summary>The one last-vote-wins rule: the last line per (pairId, judge) in file order counts. <c>score</c> and <c>serve</c> both read votes through it.</summary>
internal static class EffectiveVotes
{
    /// <summary>Pick per judge per pair id. Judge names are trimmed, so " Dean" and "Dean" are one judge.</summary>
    internal static Dictionary<string, Dictionary<string, VotePick>> Last(IReadOnlyList<Vote> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);
        Dictionary<string, Dictionary<string, VotePick>> byPair = new(StringComparer.Ordinal);
        foreach (Vote vote in votes)
        {
            if (!byPair.TryGetValue(vote.PairId, out Dictionary<string, VotePick>? picks))
            {
                picks = new Dictionary<string, VotePick>(StringComparer.Ordinal);
                byPair[vote.PairId] = picks;
            }

            picks[vote.Judge.Trim()] = vote.Pick;
        }

        return byPair;
    }
}
