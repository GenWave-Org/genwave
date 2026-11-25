using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;

namespace GenWave.Tts;

/// <summary>
/// What the rules-mode flows share (SPEC F220.9, F220.12, F220.16, F220.29), whatever the kind of talk: which verdicts are flags (sorted by
/// rule id, switched-on only), how a shaper or recorder that threw is marked on its verdict, and how an attempt's
/// <c>talk_check</c> row and a flag's <c>rule-flagged</c> booth row are written. The flows differ only in the snapshot they build,
/// the clean-up they run and what a still-flagged talk falls back to, so none of that is here.
/// </summary>
internal sealed class RulesFlowSupport(ITalkRuleBook ruleBook, ITalkCheckLog checkLog, IStationEventSink events, ILogger logger)
{
    /// <summary>The switched-on flags among <paramref name="verdicts"/>, sorted by rule id so a re-ask lists their asks in a stable order.</summary>
    public static IReadOnlyList<FlaggedRule> FlagsOf(TalkList<RuleVerdict> verdicts) => verdicts
        .Where(v => v.SwitchedOn && v.Flag is not null)
        .OrderBy(v => v.RuleId, StringComparer.Ordinal)
        .Select(v => new FlaggedRule(v, v.Flag ?? throw new InvalidOperationException("Flagged verdict without a flag.")))
        .ToList();

    /// <summary>
    /// A shaper or recorder that threw is recorded on its rule's verdict as <see cref="RuleVerdict.Broken"/> (F220.9), never as a
    /// flag: it must not change the outcome, nor a replay of the row (F220.30). A rule with no verdict of its own gets one.
    /// </summary>
    public static TalkList<RuleVerdict> Mark(TalkList<RuleVerdict> verdicts, TalkList<string> brokenRuleIds, string reason)
    {
        if (brokenRuleIds.Count == 0)
            return verdicts;

        var marked = verdicts.ToList();
        foreach (var ruleId in brokenRuleIds)
        {
            var index = marked.FindIndex(v => v.RuleId == ruleId);
            if (index < 0)
                marked.Add(new RuleVerdict(ruleId, true, null, reason));
            else
                marked[index] = marked[index] with { Broken = marked[index].Broken is { } earlier ? $"{earlier}, {reason}" : reason };
        }

        return new TalkList<RuleVerdict>(marked.OrderBy(v => v.RuleId, StringComparer.Ordinal));
    }

    /// <summary>Writes one attempt's rules-mode <c>talk_check</c> row (F220.29). The row is queued, never awaited.</summary>
    public void Write(
        long? personaId, string raw, string? cleaned, TalkSnapshot snapshot, TalkList<RuleVerdict> verdicts, TalkOutcome outcome) =>
        checkLog.TryRecord(new TalkCheckRecord(personaId, TalkCheckMode.Rules, raw, cleaned, snapshot, verdicts, null, outcome));

    /// <summary>
    /// Tells the recording rules about accepted talk (F220.13, F220.15), then writes the accepted attempt's row with any recorder
    /// that threw marked "Could not record" on its verdict (F220.9). Never throws except for the caller's own cancellation:
    /// accepted talk stays accepted, and the failure is a Warning on the owning flow's logger.
    /// </summary>
    public async ValueTask RecordAcceptedAsync(
        long? personaId, string raw, string cleaned, TalkSnapshot snapshot, TalkList<RuleVerdict> verdicts, CancellationToken ct)
    {
        try
        {
            verdicts = Mark(verdicts, await ruleBook.AcceptedAsync(snapshot, ct), TalkRuleReasons.CouldNotRecord);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recording accepted {Kind} talk failed; the talk airs as accepted", snapshot.SegmentKind);
        }

        Write(personaId, raw, cleaned, snapshot, verdicts, TalkOutcome.Accepted);
    }

    /// <summary>One <c>rule-flagged</c> booth row per flagging rule (F220.16); none when the talk has no persona to key the row on.</summary>
    public void PublishFlags(
        long? personaId, string speaker, RuleFlagTalk talk, IReadOnlyList<FlaggedRule> flags, RuleFlagOutcome outcome)
    {
        if (personaId is not { } id)
            return;

        foreach (var flagged in flags)
            events.Publish(new RuleFlagged(id, NameOf(flagged.Verdict.RuleId), speaker, outcome) { Talk = talk });
    }

    /// <summary>The rule's plain name without its closing full stop, as the booth row words it; the id when the rule is unknown.</summary>
    public string NameOf(string ruleId) =>
        ruleBook.Rules.FirstOrDefault(rule => rule.Id == ruleId)?.Describe().Name.TrimEnd('.') ?? ruleId;
}
