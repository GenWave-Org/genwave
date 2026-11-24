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
/// <see cref="JudgeBudget"/> bounds only the async parts (the switches and the snapshot): a rule's own
/// <c>Judge</c> is synchronous and takes no token, so it cannot be cut short. Only <see cref="TalkRulesMode.Shadow"/> judges: <see cref="TalkRulesMode.Rules"/> has
/// no flow yet (PLAN T800), so it airs as legacy and records nothing; a shadow row would be a lie and a rules row too.
/// </remarks>
public sealed class DjLineShadowCheck(
    IOptionsMonitor<TalkRulesOptions> options,
    ITalkSnapshotSource<DjLineSnapshotInput> snapshots,
    ITalkRuleBook ruleBook,
    ITalkCheckLog checkLog,
    ILogger<DjLineShadowCheck> logger)
{
    /// <summary>The longest one check may take before it is abandoned with a Warning.</summary>
    static readonly TimeSpan JudgeBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// True when the live mode is shadow, read fresh each call so a PUT applies to the next written talk (F220.18). An
    /// unreadable value (a live edit that fails validation) reads as legacy.
    /// </summary>
    public bool IsOn()
    {
        try
        {
            return TalkRulesModes.TryParse(options.CurrentValue.Mode) == TalkRulesMode.Shadow;
        }
        catch (Exception ex)
        {
            // Never throws: a mode that cannot be read is legacy, and the talk airs as it always did.
            logger.LogWarning(ex, "TalkRules:Mode could not be read; the rule book is not called for this talk");
            return false;
        }
    }

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
            budget.CancelAfter(JudgeBudget);

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
                OutcomeOf(legacyOutcome)));
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

    /// <summary>What the attempt came to, read off what today's code did with it.</summary>
    static TalkOutcome OutcomeOf(TalkLegacyOutcome legacy) => legacy switch
    {
        TalkLegacyOutcome.Passed or TalkLegacyOutcome.Trimmed => TalkOutcome.Accepted,
        TalkLegacyOutcome.ReAsked => TalkOutcome.ReAsked,
        TalkLegacyOutcome.Template => TalkOutcome.Template,
        TalkLegacyOutcome.Dropped => TalkOutcome.Dropped,
        _ => throw new ArgumentOutOfRangeException(nameof(legacy), legacy, "Unknown legacy outcome."),
    };
}
