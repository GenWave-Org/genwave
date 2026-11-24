using GenWave.Core.Abstractions;
using GenWave.Core.TalkRules;
using GenWave.Tts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenWave.Ads;

/// <summary>
/// The shadow hook for one ad attempt (SPEC F220.18, F220.19): the ad twin of <c>DjLineShadowCheck</c> and
/// <c>ChatShadowCheck</c>. Once today's code has decided attempt 1, the rule book judges the same reply and the check log
/// keeps both verdicts and the legacy outcome. It lives here because the ad snapshot does; <see cref="AdScriptWriter"/>
/// reaches it through <see cref="IAdShadowCheck"/>. It only observes: any fault in the snapshot, the judge or the record
/// logs a Warning and changes nothing. Only <see cref="TalkRulesMode.Shadow"/> judges.
/// </summary>
public sealed class AdShadowCheck(
    IOptionsMonitor<TalkRulesOptions> options,
    ITalkSnapshotSource<AdSnapshotInput> snapshots,
    ITalkRuleBook ruleBook,
    ITalkCheckLog checkLog,
    IStationIdentityProvider stationIdentity,
    IStationClockProvider stationClock,
    ILogger<AdShadowCheck> logger) : IAdShadowCheck
{
    /// <inheritdoc />
    public bool IsOn() => ShadowCheckSupport.IsShadow(options, logger);

    /// <inheritdoc />
    public async ValueTask CheckAsync(
        AdScriptWriteRequest request, string rawReply, string? judgedText, TalkLegacyOutcome legacyOutcome, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(ShadowCheckSupport.JudgeBudget);

            // Built inside the fault boundary: a throw while shaping the input must never reach the writer.
            var snapshot = await snapshots.BuildAsync(InputOf(request, rawReply), budget.Token);
            var verdicts = judgedText is null ? TalkList<RuleVerdict>.Empty : await ruleBook.JudgeAsync(snapshot, budget.Token);

            checkLog.TryRecord(new TalkCheckRecord(
                null, // an ad has no persona
                TalkCheckMode.Shadow,
                rawReply,
                judgedText,
                snapshot,
                verdicts,
                legacyOutcome,
                ShadowCheckSupport.OutcomeOf(legacyOutcome)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shadow talk check failed for {Kind}; the talk airs as today's code decided", TalkKinds.Ad);
        }
    }

    AdSnapshotInput InputOf(AdScriptWriteRequest request, string rawReply) => new(
        rawReply,
        AdScriptRequests.ValidationRequestFor(request),
        stationIdentity.Current.Name,
        stationClock.LocalNow,
        stationClock.Zone.Id,
        Attempt: 1);
}
