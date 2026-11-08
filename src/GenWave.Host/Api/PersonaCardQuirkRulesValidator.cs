using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Import-boundary check for <see cref="PersonaCard.QuirkRules"/> (SPEC F218.5). An imported card is
/// never rewritten (byte-stable round trip), so a rule that breaks the lockstep with
/// <see cref="PersonaCard.Quirks"/> or carries an out-of-range knob is refused with a clear message.
/// </summary>
internal static class PersonaCardQuirkRulesValidator
{
    const int MaxEchoedTextLength = 80;

    /// <summary>The first problem found in <paramref name="card"/>'s rules, or <see langword="null"/> when they are valid or absent.</summary>
    internal static string? FirstProblem(PersonaCard card)
    {
        if (card.QuirkRules is null)
            return null;

        // A card with rules but no quirks list deserializes with a null Quirks.
        IReadOnlyList<string> quirks = card.Quirks ?? [];

        foreach (var rule in card.QuirkRules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Text))
                return "Every quirk rule needs a text.";

            var echoed = Echo(rule.Text);
            if (!quirks.Contains(rule.Text, StringComparer.Ordinal))
                return $"Quirk rule \"{echoed}\" must also appear in quirks.";
            if (rule.MaxPerShift is < 1)
                return $"Quirk rule \"{echoed}\": maxPerShift must be at least 1.";
            if (rule.PerBreakChance is { } chance && !(chance > 0.0 && chance <= 1.0))
                return $"Quirk rule \"{echoed}\": perBreakChance must be above 0 and at most 1.";
        }

        return null;
    }

    // The cut may split a surrogate pair, in the echoed text only (harmless).
    static string Echo(string text) =>
        text.Length <= MaxEchoedTextLength ? text : string.Concat(text.AsSpan(0, MaxEchoedTextLength), "…");
}
