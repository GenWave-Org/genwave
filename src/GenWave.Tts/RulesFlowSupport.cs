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

    /// <summary>The reason a failed or dropped re-ask gives when it was flagged again, naming every flagged rule (F220.14).</summary>
    public static string FlaggedAgainReason(IEnumerable<string> ruleNames) =>
        $"the re-ask was flagged again ({string.Join(", ", ruleNames)})";

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
        long? personaId, string raw, string? cleaned, TalkSnapshot snapshot, TalkList<RuleVerdict> verdicts, TalkOutcome outcome)
    {
        PublishBroken(personaId, snapshot, verdicts);
        checkLog.TryRecord(new TalkCheckRecord(personaId, TalkCheckMode.Rules, raw, cleaned, snapshot, verdicts, null, outcome));
    }

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

    /// <summary>
    /// One <c>rule-flagged</c> booth row per flagging rule (F220.16), keyed on <paramref name="personaId"/> (null for an ad, which
    /// names its sponsor as <paramref name="speaker"/>). A caller whose talk needs a persona to key the row on guards before calling.
    /// </summary>
    public void PublishFlags(
        long? personaId, string speaker, RuleFlagTalk talk, IReadOnlyList<FlaggedRule> flags, RuleFlagOutcome outcome)
    {
        foreach (var flagged in flags)
            events.Publish(new RuleFlagged(personaId, NameOf(flagged.Verdict.RuleId), speaker, outcome) { Talk = talk });
    }

    /// <summary>
    /// One <c>rule-broken</c> booth row per rule step that threw on this attempt (F220.9): the ONE place a flow's breakage reaches the
    /// booth, because every attempt's row goes through <see cref="Write"/>. Rows are keyed on <paramref name="personaId"/> and name the
    /// DJ off the snapshot's speakers (a chat's host is its first), or the sponsor for an ad. A line or chat with no persona or no
    /// named speaker has no row to key it on, as <see cref="PublishFlags"/>' callers skip it.
    /// </summary>
    void PublishBroken(long? personaId, TalkSnapshot snapshot, TalkList<RuleVerdict> verdicts)
    {
        var talk = snapshot.Kind switch
        {
            TalkKinds.Chat => RuleFlagTalk.Chat,
            TalkKinds.Ad => RuleFlagTalk.Ad,
            _ => RuleFlagTalk.Line,
        };
        var speaker = talk == RuleFlagTalk.Ad
            ? snapshot.Ad?.SponsorName
            : snapshot.Speakers.FirstOrDefault(s => s.PersonaId == personaId)?.Name;
        if (string.IsNullOrEmpty(speaker) || (talk != RuleFlagTalk.Ad && personaId is null))
            return;

        foreach (var verdict in verdicts)
        {
            if (verdict.Broken is not { } broken)
                continue;

            var name = NameOf(verdict.RuleId);
            // The shaper runs once per request but marks every judged attempt, so only attempt 1 speaks for it.
            if (snapshot.Attempt == 1 && broken.Contains(TalkRuleReasons.CouldNotShape, StringComparison.Ordinal))
                events.Publish(new RuleBroken(personaId, name, speaker, RuleBrokenStep.Shape) { Talk = talk });
            if (broken.Contains(TalkRuleReasons.CouldNotRecord, StringComparison.Ordinal))
                events.Publish(new RuleBroken(personaId, name, speaker, RuleBrokenStep.Record) { Talk = talk });
        }
    }

    /// <summary>The rule's plain name, as the booth row words it; the id when the rule is unknown.</summary>
    public string NameOf(string ruleId) =>
        ruleBook.Rules.FirstOrDefault(rule => rule.Id == ruleId)?.Describe().Name ?? ruleId;
}
