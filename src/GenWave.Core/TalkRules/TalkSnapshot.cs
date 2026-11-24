using System.Text.Json;

namespace GenWave.Core.TalkRules;

/// <summary>
/// Everything a rule may look at for one written attempt. Stored as JSON, it is enough to re-judge the
/// attempt later (F220.30). <paramref name="Preview"/> is true for an operator's preview render, which is never judged for
/// repeats or catchphrase limits (legacy runs those only on air). <paramref name="ShiftKey"/> is the DJ's shift key text
/// (<c>ShiftKey.ToString()</c>), which a rule that spends a per-shift allowance needs; null when the break has none.
/// <paramref name="UtteranceId"/> is the id of the utterance row the accepted line was recorded as, for a rule that settles
/// something against it once the talk is accepted; null for an attempt that was not recorded (every snapshot before acceptance).
/// <paramref name="TasteNotes"/> are the spoken-friendly descriptions of the liked taste rules behind this break's track pick, which a
/// rule that varies repeated taste notes compares with the last break's; null when the break has none to give.
/// <paramref name="PreviousTasteNotes"/> are the notes the last on-air break carried, as the writer remembers them; null when there is none to
/// compare, so the rule that varies taste notes holds no memory of its own.
/// <paramref name="ExtraLineTaken"/> is true when the break's one extra line is already spoken for (a context fact was taken, or a
/// crosstalk airs this break), so a rule that would offer a show-flavour line must not ask for one.
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
    string? ShiftKey = null,
    long? UtteranceId = null,
    TalkList<string>? TasteNotes = null,
    bool ExtraLineTaken = false,
    TalkList<string>? PreviousTasteNotes = null)
{
    /// <summary>The memo this rule left, or null. A rule reads only its own (F220.8).</summary>
    public JsonElement? MemoOf(ITalkRule rule) => Memos.Find(rule.Id);
}
