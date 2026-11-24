using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenWave.Tts;

/// <summary>
/// The shadow hook for one crosstalk attempt (SPEC F220.18, F220.19): the chat twin of <see cref="DjLineShadowCheck"/>.
/// Once today's code has decided attempt 1, the rule book judges the same script and the check log keeps both verdicts
/// and the legacy outcome. It only observes: any fault in the snapshot, the judge or the record logs a Warning and
/// changes nothing. Only <see cref="TalkRulesMode.Shadow"/> judges (see <see cref="DjLineShadowCheck"/> for why).
/// </summary>
public sealed class ChatShadowCheck(
    IOptionsMonitor<TalkRulesOptions> options,
    ITalkSnapshotSource<ChatSnapshotInput> snapshots,
    ITalkRuleBook ruleBook,
    ITalkCheckLog checkLog,
    ILogger<ChatShadowCheck> logger)
{
    /// <summary>True when the live mode is shadow, read fresh each call (F220.18).</summary>
    public bool IsOn() => ShadowCheckSupport.IsShadow(options, logger);

    /// <summary>
    /// Judges and records attempt 1. <paramref name="cleaned"/> is null only on a shape failure (line count, tag, empty line,
    /// truncated reply): the attempt is still recorded, with no verdicts, since no whole script exists to judge (F220.29).
    /// Every other legacy discard carries the parsed script and is judged.
    /// </summary>
    public async ValueTask CheckAsync(
        CrosstalkExchangeRequest request, Func<ChatSnapshotInput> buildInput, string rawReply, string? cleaned,
        TalkLegacyOutcome legacyOutcome, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(ShadowCheckSupport.JudgeBudget);

            var snapshot = await snapshots.BuildAsync(buildInput(), budget.Token);
            var verdicts = cleaned is null ? TalkList<RuleVerdict>.Empty : await ruleBook.JudgeAsync(snapshot, budget.Token);

            checkLog.TryRecord(new TalkCheckRecord(
                request.HostPersonaId, // a chat has two voices; the row keys on the host's persona, null when the caller named none
                TalkCheckMode.Shadow,
                rawReply,
                cleaned,
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
            logger.LogWarning(ex, "Shadow talk check failed for {Kind}; the talk airs as today's code decided", TalkKinds.Chat);
        }
    }
}
