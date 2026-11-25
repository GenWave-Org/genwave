using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using GenWave.Core.TalkRules;
using GenWave.Tts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenWave.Ads;

/// <summary>
/// The rules-mode flow for one ad spot (SPEC F220.10-F220.16, F220.29): the ad twin of <c>DjLineRulesFlow</c> and
/// <c>ChatRulesFlow</c>. The model is asked, the reply gets the fixed clean-up (the writer's line-aware hygiene, then the parse and
/// stage-direction checks, with no phone rewrite), every judge runs over the same snapshot, and a flag from a switched-on rule earns
/// exactly one re-ask that lists every flagged rule's ask (sorted by rule id). A second flag fails the spot and its reason names every
/// flagged rule; an ad never airs flagged and has no template. A clean-up failure fails the spot with no rule run and no re-ask.
/// Every attempt writes a <c>talk_check</c> row; a flag that changes the outcome writes a <c>rule-flagged</c> booth row naming the
/// sponsor (an ad has no DJ).
/// </summary>
/// <remarks>
/// Lives here, not beside the other flows, because the ad snapshot does and GenWave.Tts never references this project (L10);
/// <see cref="AdScriptWriter"/> reaches it through <see cref="IAdRulesFlow"/>. The shared flow pieces are
/// <see cref="RulesFlowSupport"/>. No rule shapes ads today, so there is no shaping step and the prompts are the writer's own; the
/// first ad shaper adds it here. The duration, brand, phone and posture checks the legacy validator ran are the fits-time,
/// other-brands, phone-number and clean-language rules.
/// </remarks>
public sealed class AdRulesFlow(
    IOptionsMonitor<TalkRulesOptions> options,
    ITalkSnapshotSource<AdSnapshotInput> snapshots,
    ITalkRuleBook ruleBook,
    ITalkCheckLog checkLog,
    IStationEventSink events,
    IStationIdentityProvider stationIdentity,
    IStationClockProvider stationClock,
    ILogger<AdRulesFlow> logger) : IAdRulesFlow
{
    readonly RulesFlowSupport support = new(ruleBook, checkLog, events, logger);

    /// <inheritdoc />
    public bool IsOn() => ShadowCheckSupport.IsRules(options, logger);

    /// <inheritdoc />
    public async Task<AdRulesResult> RunAsync(AdScriptWriteRequest request, string userPrompt, AdRulesCalls calls, CancellationToken ct)
    {
        var input = new AdSnapshotInput(
            string.Empty, AdScriptRequests.ValidationRequestFor(request), stationIdentity.Current.Name,
            stationClock.LocalNow, stationClock.Zone.Id, Attempt: 1);

        var first = await AttemptAsync(input, 1, calls.Ask, userPrompt, ct);
        if (first.Result is { } decided1)
            return decided1;

        // Flagged: one re-ask naming every flagged rule's ask, sorted by rule id (F220.13). The caught call keeps its own ring entry.
        calls.RecordCaught(first.Raw);
        PublishFlags(request, first.Flags, RuleFlagOutcome.ReAsked);
        var reAsk = LlmPromptBuilder.BuildRuleReAsk(userPrompt, first.Flags.Select(f => f.Flag.Ask));
        var second = await AttemptAsync(input, 2, calls.Ask, reAsk, ct);
        if (second.Result is { } decided2)
            return decided2;

        // Still flagged: the spot is failed, and the reason names every flagged rule (F220.14).
        PublishFlags(request, second.Flags, RuleFlagOutcome.Failed);
        var names = second.Flags.Select(f => support.NameOf(f.Verdict.RuleId));
        return new AdRulesResult.StillFlagged(second.Raw, RulesFlowSupport.FlaggedAgainReason(names));
    }

    /// <inheritdoc />
    public async ValueTask AcceptedAsync(AdRulesAccepted accepted, CancellationToken ct) =>
        await support.RecordAcceptedAsync(null, accepted.Raw, accepted.Cleaned, accepted.Snapshot, accepted.Verdicts, ct);

    async Task<Attempt> AttemptAsync(
        AdSnapshotInput input, int attempt, Func<int, string, Task<AdRulesReply>> ask, string prompt, CancellationToken ct)
    {
        var reply = await ask(attempt, prompt);
        var snapshot = await snapshots.BuildAsync(input with { RawReply = reply.Raw, Attempt = attempt }, ct);

        if (reply.Fault is { } fault)
        {
            // The call faulted or left nothing whole to judge (F220.11): no rule runs and nothing is re-asked; the attempt is still recorded (F220.29).
            support.Write(null, reply.Raw, null, snapshot, TalkList<RuleVerdict>.Empty, TalkOutcome.Failed);
            return new Attempt(new AdRulesResult.Faulted(fault), [], reply.Raw);
        }

        var cleaned = reply.Cleaned ?? string.Empty;
        if (AdScriptValidator.CheckShape(cleaned, input.Request.MaxLineChars) is { } violation)
        {
            support.Write(null, reply.Raw, cleaned, snapshot, TalkList<RuleVerdict>.Empty, TalkOutcome.Failed);
            return new Attempt(new AdRulesResult.ShapeRefused(reply.Raw, violation.RuleId, violation.Reason), [], reply.Raw);
        }

        var verdicts = await ruleBook.JudgeAsync(snapshot, ct);
        var flags = RulesFlowSupport.FlagsOf(verdicts);
        if (flags.Count == 0)
        {
            // The row waits for AcceptedAsync, so the recorders' breakage can be on it.
            return new Attempt(
                new AdRulesResult.Accepted(reply.Raw, cleaned, new AdRulesAccepted(reply.Raw, cleaned, snapshot, verdicts)), [], reply.Raw);
        }

        support.Write(null, reply.Raw, cleaned, snapshot, verdicts, attempt == 1 ? TalkOutcome.ReAsked : TalkOutcome.Failed);
        return new Attempt(null, flags, reply.Raw);
    }

    /// <summary>An ad names its sponsor: it has no persona to key the row on, nor a DJ to name.</summary>
    void PublishFlags(AdScriptWriteRequest request, IReadOnlyList<FlaggedRule> flags, RuleFlagOutcome outcome) =>
        support.PublishFlags(null, request.SponsorName, RuleFlagTalk.Ad, flags, outcome);

    sealed record Attempt(AdRulesResult? Result, IReadOnlyList<FlaggedRule> Flags, string Raw);
}
