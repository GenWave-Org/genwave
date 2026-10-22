namespace GenWave.VoiceOff.Listening;

/// <summary>Reads <c>votes.jsonl</c> in file order. Last-vote-wins is the scorer's job; this keeps every line.</summary>
internal static class VotesReader
{
    /// <exception cref="InvalidDataException">A line is malformed, has a blank judge, or names a pair the manifest does not have.</exception>
    internal static Task<IReadOnlyList<Vote>> ReadAsync(string path, string label, PairManifest manifest, CancellationToken ct)
    {
        HashSet<string> pairIds = manifest.Pairs.Select(p => p.PairId).ToHashSet(StringComparer.Ordinal);
        return JsonLines.ReadAsync<Vote>(path, label, vote =>
        {
            if (string.IsNullOrWhiteSpace(vote.Judge))
            {
                return "the judge is blank.";
            }

            return pairIds.Contains(vote.PairId) ? null : $"pairId '{vote.PairId}' is not in the manifest.";
        }, ct);
    }
}
