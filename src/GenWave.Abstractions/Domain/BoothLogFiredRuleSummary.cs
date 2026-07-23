namespace GenWave.Core.Domain;

/// <summary>
/// One <see cref="TasteRule"/> reduced to exactly what SPEC F86.1's booth-log pick stamp keeps: a
/// human-readable label of what the rule matched, and its signed weight. Deliberately narrower than
/// <see cref="TasteRule"/> itself — the predicate/context that decided whether the rule fired is not
/// persisted, only the fact that it did and how strongly.
/// </summary>
public sealed record BoothLogFiredRuleSummary(string Summary, double Weight)
{
    /// <summary>
    /// Artist over genre over tag — the SAME precedence <c>Orchestrator.FormatFiredRule</c>'s debug
    /// line and <c>LlmPromptBuilder.DescribeFiredRule</c>'s copywriter phrasing already use for a
    /// fired <see cref="TasteRule"/> (a rule's predicate fields are AND-matched, but in practice a
    /// rule opinions about exactly one of them). The fallback label for the theoretical all-null
    /// predicate is NOT shared, though: <c>Orchestrator.FormatFiredRule</c> falls back to "any" (a
    /// log token), while this stamp deliberately follows <c>LlmPromptBuilder.DescribeFiredRule</c>'s
    /// "this pick" instead — the persisted summary is operator-facing prose, like the spoken line,
    /// not a debug-log token.
    /// </summary>
    public static BoothLogFiredRuleSummary FromTasteRule(TasteRule rule) =>
        new(rule.Predicate.LabelOr("this pick"), rule.Weight);
}
