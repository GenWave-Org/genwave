namespace GenWave.Tts;

/// <summary>
/// Names WHICH check a candidate pronunciation rule fails, reusing the EXACT filter
/// <see cref="PronunciationRuleSet.Create"/> applies at compile time (SPEC F97.1, F97.5, T137/T138
/// review findings) rather than re-deriving it — <see cref="PronunciationRuleSet.Create"/> itself
/// calls <see cref="IsValid"/> for its own drop predicate, so the render path and the write path
/// (T144's rules API) can never disagree about what compiles.
///
/// <see cref="PronunciationRuleSet.Create"/> degrades a bad rule SILENTLY, because one operator-
/// authored rule must never take a whole render down; <see cref="Validate"/> exists for the opposite
/// posture — an operator saving THIS ONE rule through the API deserves a 400 naming the offending
/// field, not a rule that silently never fires (F97.5's "declared-vs-compiled honesty", extended from
/// the <see cref="PronunciationRuleProvider"/> WARN log to the write path itself).
///
/// <see cref="IsValid"/> and <see cref="Validate"/> share the SAME per-check predicates below (T144
/// review round 2 residual #4/#5) — <see cref="IsValid"/> is the fast yes/no path
/// <see cref="PronunciationRuleSet.Create"/> calls for every declared rule on every
/// <c>Tts:Pronunciations</c>/card refresh (station settings reload, or
/// <c>ActivePersonaPronunciationRulesCache</c>'s own ~30s poll): it allocates no
/// <see cref="List{T}"/>, unlike calling <see cref="Validate"/> and checking
/// <c>.Count == 0</c> would. <see cref="Validate"/> stays the field-named path for the two callers
/// that genuinely need the messages — the write-path 400 and <c>PronunciationsController.BuildRows</c>'s
/// dropped-row <c>Reason</c>.
/// </summary>
public static class PronunciationRuleValidator
{
    /// <summary>
    /// The fast yes/no path: <see langword="true"/> exactly when <see cref="Validate"/> would return
    /// no errors, without allocating one. Every condition mirrors <see cref="Validate"/>'s own — see
    /// its remarks for what each one means.
    /// </summary>
    public static bool IsValid(string pattern, string? word, string ipa)
    {
        var rule = Resolve(pattern, word, ipa);

        if (PatternIsBlank(rule) || WordIsBlank(rule))
            return false;

        if (IpaIsBlank(rule) || IpaHasDisallowedBracket(rule))
            return false;

        return !WordNotInPattern(rule);
    }

    /// <summary>
    /// Checks <paramref name="pattern"/>/<paramref name="word"/>/<paramref name="ipa"/> against every
    /// condition <see cref="PronunciationRuleSet.Create"/> would silently drop the rule for — a blank
    /// pattern, a blank word (after <see cref="PronunciationRule.Parse"/>'s own pattern-default), a
    /// blank ipa (after <see cref="PronunciationRuleSet.CanonicalizeIpa"/>'s slash/whitespace trim), an
    /// ipa carrying <c>)</c>/<c>[</c>/<c>]</c>, or a word that does not occur inside its own pattern.
    /// Returns an empty list when the rule would compile cleanly (see <see cref="IsValid"/> for that
    /// yes/no question without the allocation this carries).
    /// </summary>
    public static IReadOnlyList<PronunciationRuleValidationError> Validate(string pattern, string? word, string ipa)
    {
        var rule = Resolve(pattern, word, ipa);
        var errors = new List<PronunciationRuleValidationError>();

        if (PatternIsBlank(rule))
            errors.Add(new PronunciationRuleValidationError("pattern", "Pattern must not be blank."));

        if (WordIsBlank(rule))
            errors.Add(new PronunciationRuleValidationError("word", "Word must not be blank."));

        if (IpaIsBlank(rule))
        {
            errors.Add(new PronunciationRuleValidationError(
                "ipa", "Ipa must not be blank after trimming slash delimiters and whitespace."));
        }
        else if (IpaHasDisallowedBracket(rule))
        {
            errors.Add(new PronunciationRuleValidationError("ipa", "Ipa must not contain ')', '[', or ']'."));
        }

        if (!PatternIsBlank(rule) && !WordIsBlank(rule) && WordNotInPattern(rule))
            errors.Add(new PronunciationRuleValidationError("word", "Word must occur within Pattern."));

        return errors;
    }

    static PronunciationRule Resolve(string pattern, string? word, string ipa) =>
        PronunciationRule.Parse(pattern, word, PronunciationRuleSet.CanonicalizeIpa(ipa));

    static bool PatternIsBlank(PronunciationRule rule) => string.IsNullOrWhiteSpace(rule.Pattern);

    static bool WordIsBlank(PronunciationRule rule) => string.IsNullOrWhiteSpace(rule.Word);

    static bool IpaIsBlank(PronunciationRule rule) => string.IsNullOrWhiteSpace(rule.Ipa);

    static bool IpaHasDisallowedBracket(PronunciationRule rule) =>
        rule.Ipa.Contains(')') || rule.Ipa.Contains('[') || rule.Ipa.Contains(']');

    static bool WordNotInPattern(PronunciationRule rule) =>
        !rule.Pattern.Contains(rule.Word, StringComparison.OrdinalIgnoreCase);
}
