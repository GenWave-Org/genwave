namespace GenWave.Core.Domain;

/// <summary>
/// One authored taste opinion carried in a <see cref="PersonaCard"/>'s <see cref="PersonaCard.Taste"/>
/// (SPEC F79.1, F79.2, F82.1) — additive to the F71.1 card shape; the card stays schema major 1. A
/// card only ever carries <c>source='authored'</c> rules (F79.1) — operator-nudged and accrued rows
/// live solely in the station's own <c>persona_taste</c> table (ARCHITECTURE.md "Personalities on
/// air") and are never exported.
/// </summary>
/// <param name="Predicate">AND-matched match criteria (artist/genre/tag) a candidate must satisfy for this rule to fire.</param>
/// <param name="Context">Day-of-week/hour gate the rule is scoped to; unbounded fields match at any time.</param>
/// <param name="Weight">
/// Bias strength in <c>[-1, 1]</c> (SPEC F82.1's <c>CHECK (weight BETWEEN -1 AND 1)</c>). Negative
/// weights are dislikes — first-class taste, not an error.
/// </param>
/// <exception cref="ArgumentOutOfRangeException">
/// Thrown when <paramref name="Weight"/> falls outside <c>[-1, 1]</c> — enforced on every
/// construction path, including JSON deserialization (which invokes this same constructor).
/// </exception>
public sealed record TasteRule(TastePredicate Predicate, TasteContext Context, double Weight)
{
    public double Weight { get; init; } = Weight is >= -1.0 and <= 1.0
        ? Weight
        : throw new ArgumentOutOfRangeException(nameof(Weight), Weight, "Weight must be within [-1, 1] (SPEC F82.1).");
}
