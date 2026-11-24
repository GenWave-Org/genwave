using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

internal static class CheckEntries
{
    public static readonly DateTimeOffset WrittenAt = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    public static readonly RuleFlag Repeated = new("Repeated", "Say something new.", "said before");

    /// <summary>A stored attempt. <paramref name="verdicts"/> are (rule id, flag) pairs, all recorded switched on.</summary>
    public static TalkCheckEntry Entry(
        long id, TalkSnapshot snapshot, TalkLegacyOutcome? legacy = null, params (string RuleId, RuleFlag? Flag)[] verdicts) =>
        new(
            id,
            WrittenAt,
            new TalkCheckRecord(
                1,
                legacy is null ? TalkCheckMode.Rules : TalkCheckMode.Shadow,
                "raw",
                "cleaned",
                snapshot,
                new TalkList<RuleVerdict>(verdicts.Select(v => new RuleVerdict(v.RuleId, true, v.Flag))),
                legacy,
                TalkOutcome.Accepted));

    /// <summary>A stored attempt nothing was judged for: no cleaned text, no verdicts, but the snapshot is kept.</summary>
    public static TalkCheckEntry Unjudged(long id, TalkSnapshot snapshot, TalkLegacyOutcome? legacy = null)
    {
        var entry = Entry(id, snapshot, legacy);
        return entry with { Record = entry.Record with { Cleaned = null, Verdicts = TalkList<RuleVerdict>.Empty } };
    }

    public static TalkSnapshot Ad(string text) => TalkSnapshots.DjLine(text) with { Kind = TalkKinds.Ad, SegmentKind = "Ad" };
}
