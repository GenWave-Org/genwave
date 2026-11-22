using System.Text.Json;

namespace GenWave.Core.TalkRules;

/// <summary>
/// Everything a rule may look at for one written attempt. Stored as JSON, it is enough to re-judge the
/// attempt later (F220.30).
/// </summary>
public sealed record TalkSnapshot(
    TalkKinds Kind,
    string SegmentKind,
    int Attempt,
    TalkList<TalkLine> Lines,
    TalkList<TalkSpeaker> Speakers,
    TalkList<string>? Facts,
    string? OwnerMessageCore,
    DateTimeOffset LocalNow,
    string Timezone,
    TalkTrack? Track,
    TalkStation Station,
    TalkList<TalkRecentLine> RecentLines,
    TalkAdFields? Ad,
    TalkMemos Memos)
{
    /// <summary>The memo this rule left, or null. A rule reads only its own (F220.8).</summary>
    public JsonElement? MemoOf(ITalkRule rule) => Memos.Find(rule.Id);
}
