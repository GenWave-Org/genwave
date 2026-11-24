using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;

namespace GenWave.TalkRules;

/// <summary>
/// Runs the rules for one kind of talk, all over the same snapshot (F220.7). Judging records a verdict for every
/// judge that covers the kind, switched-off ones included (their verdicts are recorded, not used). A rule that
/// throws never stops the others: a judge becomes a flag "Could not check" (F220.9), a shaper or recorder is
/// skipped (until T795 records it as "Could not shape" / "Could not record", SPEC F220.9); each is logged at Warning with the rule id.
/// </summary>
public sealed class TalkRuleBook(
    IEnumerable<ITalkRule> rules,
    ITalkRuleSwitches switches,
    ILogger<TalkRuleBook> logger) : ITalkRuleBook
{
    /// <summary>The reason on the flag of a judge that threw.</summary>
    public const string CouldNotCheck = "Could not check";

    internal const string CouldNotCheckAsk = "Write it again, keeping to every rule.";

    private readonly IReadOnlyList<ITalkRule> rules =
        rules.OrderBy(rule => rule.Id, StringComparer.Ordinal).ToList();

    /// <summary>Every rule the book knows, sorted by id; what the rules API lists.</summary>
    public IReadOnlyList<ITalkRule> Rules => rules;

    public async ValueTask<TalkList<RuleVerdict>> JudgeAsync(TalkSnapshot after, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var stored = await switches.GetAllAsync(after.Kind, ct);
        var verdicts = rules
            .OfType<IJudgingTalkRule>()
            .Where(rule => rule.CanCover.HasFlag(after.Kind))
            .Select(rule => new RuleVerdict(rule.Id, IsSwitchedOn(rule, after.Kind, stored), JudgeSafely(rule, after)));
        return new TalkList<RuleVerdict>(verdicts);
    }

    public async ValueTask<TalkShaping> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        var shapers = await SwitchedOnAsync<IShapingTalkRule>(before.Kind, ct);
        var results = await Task.WhenAll(shapers.Select(rule => ShapeSafelyAsync(rule, before, ct)));

        var notes = new List<string>();
        var memos = before.Memos;
        foreach (var (rule, shaping) in results.OrderBy(r => r.Rule.Id, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(shaping?.PromptNote))
            {
                notes.Add(shaping.PromptNote);
            }

            if (shaping?.Memo is { } memo)
            {
                memos = memos.With(rule.Id, memo);
            }
        }

        return new TalkShaping(new TalkList<string>(notes), memos);
    }

    public async ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct)
    {
        var recorders = await SwitchedOnAsync<IRecordingTalkRule>(accepted.Kind, ct);
        await Task.WhenAll(recorders.Select(rule => RecordSafelyAsync(rule, accepted, ct)));
    }

    private static bool IsSwitchedOn(ITalkRule rule, TalkKinds kind, IReadOnlyDictionary<string, bool> stored) =>
        stored.TryGetValue(rule.Id, out var on) ? on : rule.OnByDefault.HasFlag(kind);

    private async ValueTask<List<T>> SwitchedOnAsync<T>(TalkKinds kind, CancellationToken ct)
        where T : class, ITalkRule
    {
        var stored = await switches.GetAllAsync(kind, ct);
        return rules
            .OfType<T>()
            .Where(rule => rule.CanCover.HasFlag(kind) && IsSwitchedOn(rule, kind, stored))
            .ToList();
    }

    private RuleFlag? JudgeSafely(IJudgingTalkRule rule, TalkSnapshot after)
    {
        try
        {
            return rule.Judge(after);
        }
        catch (Exception ex)
        {
            // Judge is synchronous and takes no token, so even an OperationCanceledException here is a broken rule.
            logger.LogWarning(ex, "Talk rule {RuleId} threw while judging; counted as a flag", rule.Id);
            return new RuleFlag(CouldNotCheck, CouldNotCheckAsk, string.Empty);
        }
    }

    private async Task<(ITalkRule Rule, RuleShaping? Shaping)> ShapeSafelyAsync(
        IShapingTalkRule rule, TalkSnapshot before, CancellationToken ct)
    {
        try
        {
            return (rule, await rule.ShapeAsync(before, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Talk rule {RuleId} threw while shaping; skipped", rule.Id);
            return (rule, null);
        }
    }

    private async Task RecordSafelyAsync(IRecordingTalkRule rule, TalkSnapshot accepted, CancellationToken ct)
    {
        try
        {
            await rule.AcceptedAsync(accepted, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Talk rule {RuleId} threw while recording accepted talk; skipped", rule.Id);
        }
    }
}
