using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;

namespace GenWave.TalkRules;

/// <summary>
/// Re-judges stored snapshots with today's rules (SPEC F220.20, F220.26, F220.30). Pure over the store read: no LLM call, no write,
/// no switch lookup. It judges every registered judge whose <see cref="ITalkRule.CanCover"/> includes a row's kind, switched on or
/// not (a stored verdict is recorded for off rules too), so a rule can be checked against last week before it is switched on.
/// The log is read a keyset page at a time, never in one piece.
/// <para>A row whose <see cref="TalkCheckRecord.Cleaned"/> is null had nothing judged when it was written (its verdicts are empty although a snapshot
/// is kept), so it is skipped in both modes before any tally: re-judging it would report a change with no rule change.</para>
/// <para>Without compare, a change is a rule whose replayed flag differs from the one stored on the row (a rule with no stored verdict counts as stored null).</para>
/// <para>With compare, only rows with a legacy outcome (shadow rows) are looked at, and the stored-vs-replayed changes are not reported. Legacy rejected
/// means an outcome other than <see cref="TalkLegacyOutcome.Passed"/>; the rules rejected means any replayed flag. A rule that flags a row legacy
/// passed is a change under that rule; a row legacy rejected that no rule flags is one change under <see cref="NoRuleFlagged"/>. Rows both agree on are not listed.</para>
/// </summary>
public sealed class TalkRuleReplay(
    IEnumerable<ITalkRule> rules,
    ITalkCheckStore store,
    ILogger<TalkRuleReplay> logger) : ITalkRuleReplay
{
    /// <summary>The reserved rule id on a change where legacy rejected a line and no rule flagged it. No real rule may use it.</summary>
    public const string NoRuleFlagged = "none";

    private const int PageSize = ITalkCheckStore.MaxPageSize;

    private readonly IReadOnlyList<IJudgingTalkRule> judges =
        rules.OfType<IJudgingTalkRule>().OrderBy(rule => rule.Id, StringComparer.Ordinal).ToList();

    public async ValueTask<TalkReplayReport> ReplayAsync(TalkReplayRequest request, CancellationToken ct)
    {
        var tally = new Tally(judges.Where(judge => (judge.CanCover & request.Kinds) != TalkKinds.None).Select(judge => judge.Id));
        var changes = new List<TalkVerdictChange>();
        var trimSaved = 0;

        long afterId = 0;
        while (true)
        {
            var page = await store.ReadPageAsync(request.Since, afterId, PageSize, ct);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var entry in page)
            {
                if (entry.Record.Cleaned is null
                    || !request.Kinds.HasFlag(entry.Record.Snapshot.Kind)
                    || (request.CompareLegacy && entry.Record.LegacyOutcome is null))
                {
                    continue;
                }

                var replayed = Rejudge(entry.Record.Snapshot, tally);
                if (request.CompareLegacy)
                {
                    trimSaved += CompareLegacy(entry, replayed, changes);
                }
                else
                {
                    CompareStored(entry, replayed, changes);
                }
            }

            afterId = page[^1].Id;
        }

        return new TalkReplayReport(
            new TalkList<TalkRuleReplayCount>(tally.Counts()), new TalkList<TalkVerdictChange>(changes), trimSaved);
    }

    private List<(string RuleId, RuleFlag? Flag)> Rejudge(TalkSnapshot snapshot, Tally tally)
    {
        var results = new List<(string, RuleFlag?)>();
        foreach (var judge in judges.Where(judge => judge.CanCover.HasFlag(snapshot.Kind)))
        {
            var flag = JudgeSafely(judge, snapshot);
            tally.Add(judge.Id, flag is not null);
            results.Add((judge.Id, flag));
        }

        return results;
    }

    private static void CompareStored(TalkCheckEntry entry, List<(string RuleId, RuleFlag? Flag)> replayed, List<TalkVerdictChange> changes)
    {
        foreach (var (ruleId, flag) in replayed)
        {
            var stored = StoredFlag(entry, ruleId);
            if (!Equals(stored, flag))
            {
                changes.Add(new TalkVerdictChange(entry.Id, ruleId, stored, flag, entry.Record.LegacyOutcome));
            }
        }
    }

    /// <summary>Adds the differing lines to <paramref name="changes"/>; returns 1 when the row is a line a trim saved that a rule would have flagged.</summary>
    private static int CompareLegacy(TalkCheckEntry entry, List<(string RuleId, RuleFlag? Flag)> replayed, List<TalkVerdictChange> changes)
    {
        if (entry.Record.LegacyOutcome is not { } legacy)
        {
            return 0;
        }

        var flagged = replayed.Where(result => result.Flag is not null).ToList();
        if (legacy == TalkLegacyOutcome.Passed)
        {
            foreach (var (ruleId, flag) in flagged)
            {
                changes.Add(new TalkVerdictChange(entry.Id, ruleId, StoredFlag(entry, ruleId), flag, legacy));
            }

            return 0;
        }

        if (flagged.Count == 0)
        {
            changes.Add(new TalkVerdictChange(entry.Id, NoRuleFlagged, null, null, legacy));
            return 0;
        }

        // An ad's Trimmed is the phone rewrite, counted elsewhere, not a line a trim saved.
        return legacy == TalkLegacyOutcome.Trimmed && entry.Record.Snapshot.Kind != TalkKinds.Ad ? 1 : 0;
    }

    private static RuleFlag? StoredFlag(TalkCheckEntry entry, string ruleId) =>
        entry.Record.Verdicts.FirstOrDefault(verdict => verdict.RuleId == ruleId)?.Flag;

    private RuleFlag? JudgeSafely(IJudgingTalkRule judge, TalkSnapshot snapshot)
    {
        try
        {
            return judge.Judge(snapshot);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Talk rule {RuleId} threw while replaying; counted as a flag", judge.Id);
            return new RuleFlag(TalkRuleBook.CouldNotCheck, TalkRuleBook.CouldNotCheckAsk, string.Empty);
        }
    }

    private sealed class Tally(IEnumerable<string> ruleIds)
    {
        private readonly SortedDictionary<string, (int Checked, int Flagged)> counts =
            new(ruleIds.ToDictionary(id => id, _ => (0, 0)), StringComparer.Ordinal);

        public void Add(string ruleId, bool flagged)
        {
            var (seen, flags) = counts[ruleId];
            counts[ruleId] = (seen + 1, flags + (flagged ? 1 : 0));
        }

        public IEnumerable<TalkRuleReplayCount> Counts() =>
            counts.Select(pair => new TalkRuleReplayCount(pair.Key, pair.Value.Checked, pair.Value.Flagged));
    }
}
