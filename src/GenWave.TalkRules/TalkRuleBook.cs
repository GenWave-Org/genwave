using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;

namespace GenWave.TalkRules;

/// <summary>
/// Runs the rules for one kind of talk, all over the same snapshot (F220.7). Judging records a verdict for every
/// judge that covers the kind, switched-off ones included (their verdicts are recorded, not used). A rule that
/// throws never stops the others: a judge becomes a flag "Could not check" (F220.9); a shaper or recorder that
/// throws is reported by id ("Could not shape" / "Could not record") and never changes the outcome. Each is
/// logged at Warning with the rule id. Only the caller's own cancellation escapes.
/// </summary>
public sealed class TalkRuleBook(
    IEnumerable<ITalkRule> rules,
    ITalkRuleSwitches switches,
    ILogger<TalkRuleBook> logger) : ITalkRuleBook
{
    /// <summary>The reason on the flag of a judge that threw.</summary>
    public const string CouldNotCheck = "Could not check";

    /// <summary>What a shaper that threw is reported as (recorded on its verdict by the rules-mode flow, T800–T802).</summary>
    public const string CouldNotShape = "Could not shape";

    /// <summary>What a recorder that threw is reported as (recorded on its verdict by the rules-mode flow, T800–T802).</summary>
    public const string CouldNotRecord = "Could not record";

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
        var failed = new List<string>();
        var memos = before.Memos;
        foreach (var (rule, shaping, threw) in results.OrderBy(r => r.Rule.Id, StringComparer.Ordinal))
        {
            if (threw)
            {
                failed.Add(rule.Id);
            }

            if (!string.IsNullOrWhiteSpace(shaping?.PromptNote))
            {
                notes.Add(shaping.PromptNote);
            }

            if (shaping?.Memo is { } memo)
            {
                memos = memos.With(rule.Id, memo);
            }
        }

        return new TalkShaping(new TalkList<string>(notes), memos, new TalkList<string>(failed));
    }

    public async ValueTask<TalkList<string>> AcceptedAsync(TalkSnapshot accepted, CancellationToken ct)
    {
        var recorders = await SwitchedOnAsync<IRecordingTalkRule>(accepted.Kind, ct);
        var failed = await Task.WhenAll(recorders.Select(rule => RecordSafelyAsync(rule, accepted, ct)));
        return new TalkList<string>(failed.OfType<string>().Order(StringComparer.Ordinal));
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

    private static bool IsRuleFailure(Exception ex, CancellationToken ct) =>
        !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private async Task<(ITalkRule Rule, RuleShaping? Shaping, bool Threw)> ShapeSafelyAsync(
        IShapingTalkRule rule, TalkSnapshot before, CancellationToken ct)
    {
        try
        {
            return (rule, await rule.ShapeAsync(before, ct), false);
        }
        catch (Exception ex) when (IsRuleFailure(ex, ct))
        {
            logger.LogWarning(ex, "Talk rule {RuleId} threw while shaping; no note, outcome unchanged", rule.Id);
            return (rule, null, true);
        }
    }

    /// <summary>The rule's id when it threw, else null.</summary>
    private async Task<string?> RecordSafelyAsync(IRecordingTalkRule rule, TalkSnapshot accepted, CancellationToken ct)
    {
        try
        {
            await rule.AcceptedAsync(accepted, ct);
            return null;
        }
        catch (Exception ex) when (IsRuleFailure(ex, ct))
        {
            logger.LogWarning(ex, "Talk rule {RuleId} threw while recording accepted talk; outcome unchanged", rule.Id);
            return rule.Id;
        }
    }
}
