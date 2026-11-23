using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

internal static class TalkSnapshots
{
    public static TalkSnapshot DjLine(string text, TalkMemos? memos = null) =>
        new(
            TalkKinds.DjLine,
            "Banter",
            1,
            new TalkList<TalkLine>([new TalkLine("Flip", text)]),
            new TalkList<TalkSpeaker>([new TalkSpeaker(1, "Flip", TalkList<string>.Empty)]),
            null,
            null,
            new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(-6)),
            "America/Edmonton",
            null,
            new TalkStation("GenWave", "friendly"),
            TalkList<TalkRecentLine>.Empty,
            null,
            memos ?? TalkMemos.Empty);
}
