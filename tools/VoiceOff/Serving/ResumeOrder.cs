using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>Moves the pairs a judge has already voted to the front, so a page that starts at the voted count lands on the first unvoted pair. Each group keeps its shuffled order.</summary>
internal static class ResumeOrder
{
    internal static IReadOnlyList<SessionPair> For(IReadOnlyList<SessionPair> shuffled, IReadOnlySet<string> voted) =>
        [.. shuffled.Where(p => voted.Contains(p.PairId)), .. shuffled.Where(p => !voted.Contains(p.PairId))];
}
