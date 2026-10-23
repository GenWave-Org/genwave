using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>The pairs of one session a judge has an effective vote on. <c>/session</c> and <c>/progress</c> both read this, so the resume order and the progress count cannot drift apart.</summary>
internal static class JudgeVotes
{
    internal static async Task<IReadOnlySet<string>> VotedPairsAsync(VoteLog log, SessionEntry session, string judge, CancellationToken ct)
    {
        Dictionary<string, Dictionary<string, VotePick>> votes = await log.ReadEffectiveAsync(ct);
        return session.Pairs.Select(p => p.PairId)
            .Where(id => votes.TryGetValue(id, out Dictionary<string, VotePick>? picks) && picks.ContainsKey(judge))
            .ToHashSet(StringComparer.Ordinal);
    }
}
