using GenWave.Core.TalkRules;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Wraps the real rule book to count how often it is asked to judge; optionally throws instead, as a faulting rule book would.
/// </summary>
sealed class ObservedRuleBook(ITalkRuleBook inner, bool throwOnJudge) : ITalkRuleBook
{
    int judgeCalls;

    public IReadOnlyList<ITalkRule> Rules => inner.Rules;

    public ValueTask<IReadOnlyDictionary<string, bool>> SwitchesAsync(TalkKinds kind, CancellationToken ct) => inner.SwitchesAsync(kind, ct);

    public int JudgeCalls => Volatile.Read(ref judgeCalls);

    public ValueTask<TalkShaping> ShapeAsync(TalkSnapshot before, CancellationToken ct) => inner.ShapeAsync(before, ct);

    public ValueTask<TalkList<RuleVerdict>> JudgeAsync(TalkSnapshot after, CancellationToken ct)
    {
        Interlocked.Increment(ref judgeCalls);
        return throwOnJudge
            ? throw new InvalidOperationException("The rule book faulted.")
            : inner.JudgeAsync(after, ct);
    }

    public ValueTask<TalkList<string>> AcceptedAsync(TalkSnapshot accepted, CancellationToken ct) => inner.AcceptedAsync(accepted, ct);
}
