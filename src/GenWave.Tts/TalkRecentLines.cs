using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>The recent-line list shared by every snapshot source.</summary>
internal static class TalkRecentLines
{
    /// <summary>Every row once each (by id), newest first. Fallbacks stay, flagged; each rule picks its own window.</summary>
    public static TalkList<TalkRecentLine> Of(IEnumerable<UtteranceEntry> rows) =>
        new(rows
            .DistinctBy(e => e.Id)
            .OrderByDescending(e => e.WrittenAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new TalkRecentLine(e.PersonaId, e.Text, e.WrittenAt, e.Fallback)));
}
