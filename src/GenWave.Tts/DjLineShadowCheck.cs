using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenWave.Tts;

/// <summary>
/// The shadow hook for one DJ-line attempt (SPEC F220.18, F220.19): once today's code has decided, the rule book judges
/// attempt 1 of the same reply and the check log keeps both verdicts and the legacy outcome. It only ever observes: it
/// returns nothing the writer reads, and any fault in the snapshot, the judge or the record logs a Warning and changes nothing.
/// </summary>
/// <remarks>
/// Runs inline, not fire-and-forget: the rules are pure and fast, the row is queued and never awaited
/// (<see cref="ITalkCheckLog.TryRecord"/>), and an inline call leaves no unobserved task to lose at shutdown. The
/// <see cref="ShadowCheckSupport.JudgeBudget"/> bounds only the async parts (the switches and the snapshot): a rule's own
/// <c>Judge</c> is synchronous and takes no token, so it cannot be cut short. Only <see cref="TalkRulesMode.Shadow"/> judges: rules mode has its own flow (<see cref="DjLineRulesFlow"/>, PLAN T800), which
/// writes its own rows, so this hook stays silent there.
/// </remarks>
public sealed class DjLineShadowCheck(
    IOptionsMonitor<TalkRulesOptions> options,
    ITalkSnapshotSource<DjLineSnapshotInput> snapshots,
    ITalkRuleBook ruleBook,
    ITalkCheckLog checkLog,
    ILogger<DjLineShadowCheck> logger)
{
    /// <summary>True when the live mode is shadow (F220.18); see <see cref="ShadowCheckSupport.IsShadow"/>.</summary>
    public bool IsOn() => ShadowCheckSupport.IsShadow(options, logger);

    /// <summary>
    /// Judges and records attempt 1. <paramref name="cleaned"/> is null when clean-up failed: the attempt is still recorded,
    /// with no verdicts, since there is no line to judge (F220.29).
    /// </summary>
    public async ValueTask CheckAsync(
        SegmentRequest request, Func<DjLineSnapshotInput> buildInput, string rawReply, string? cleaned, TalkLegacyOutcome legacyOutcome, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(ShadowCheckSupport.JudgeBudget);

            var snapshot = await snapshots.BuildAsync(buildInput(), budget.Token);
            var verdicts = cleaned is null ? TalkList<RuleVerdict>.Empty : await ruleBook.JudgeAsync(snapshot, budget.Token);

            checkLog.TryRecord(new TalkCheckRecord(
                request.PersonaId, // the same id the utterance row carries; null for a persona-less preview
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
            logger.LogWarning(ex, "Shadow talk check failed for {Kind}; the talk airs as today's code decided", request.Kind);
        }
    }
}
