using System.Text.Json;

namespace GenWave.Core.TalkRules;

/// <summary>
/// Everything a rule may look at for one written attempt. Stored as JSON, it is enough to re-judge the
/// attempt later (F220.30). <paramref name="Preview"/> is true for an operator's preview render, which is never judged for
/// repeats or catchphrase limits (legacy runs those only on air). <paramref name="ShiftKey"/> is the DJ's shift key text
/// (<c>ShiftKey.ToString()</c>), which a rule that spends a per-shift allowance needs; null when the break has none.
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
    TalkMemos Memos,
    TalkChatFields? Chat = null,
    bool Preview = false,
    string? ShiftKey = null)
{
    /// <summary>The memo this rule left, or null. A rule reads only its own (F220.8).</summary>
    public JsonElement? MemoOf(ITalkRule rule) => Memos.Find(rule.Id);
}
