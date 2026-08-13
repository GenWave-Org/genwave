namespace GenWave.Tts;

using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

/// <summary>
/// THE precedence invariant between the two operator pronunciation systems (gh-#491, Dean's 2026-08-13
/// ruling): when a speech correction (<see cref="SpeechCorrection"/>, the legacy respelling mechanism,
/// SPEC F68.5) and a pronunciation rule (<see cref="PronunciationRuleSet"/>, the IPA mechanism, SPEC
/// F97) both target the SAME word, <b>the pronunciation rule wins and the correction is suppressed
/// for that render</b> — loudly, never silently (the render path logs each suppression at
/// Information; the rules API warns at authoring time).
///
/// <para>
/// <b>Why suppression exists at all:</b> corrections rewrite text at the
/// <see cref="NormalizingTtsSynthesizer"/> chokepoint BEFORE pronunciation rules ever match
/// (<see cref="KokoroSpeechMarkup"/> composes its annotations at Kokoro request build, below that
/// chokepoint — deliberately, so normalized text and every upstream cache key stay byte-identical,
/// F68.1/F70.4). Without this invariant, a correction whose <see cref="SpeechCorrection.From"/> names
/// the same word as a rule's <see cref="PronunciationRule.Pattern"/> rewrites that word out of the
/// text first, and the rule — including an audition's own unsaved candidate (SPEC F126.1) — silently
/// matches nothing, forever. That is exactly gh-#491: the operator iterated IPA against a word an old
/// correction was quietly rewriting, and every audition rendered identically.
/// </para>
///
/// <para>
/// <b>The collision predicate is identity equality</b> — <see cref="SpeechCorrection.From"/> equals
/// <see cref="PronunciationRule.Pattern"/>, case-insensitive, the same ordinal-ignore-case posture
/// both systems already match text with. Deliberately NOT containment or span overlap: a correction
/// for a longer phrase (<c>"MacLeod Duncan"</c>) that embeds a rule's word keeps applying — the
/// operator layered a more specific rewrite over a general rule, the same specific-over-general
/// composition both systems document individually — and widening the predicate would need
/// span-level protection threaded through <see cref="SpeechCorrectionSet.Apply"/> for a collision
/// class nobody has hit. A context-CONDITIONED correction (gh-#161) with an equal <c>From</c> IS
/// suppressed like any other: exempting it would recreate gh-#491 for exactly the occurrences the
/// condition covers.
/// </para>
///
/// <para>
/// <b>Engine caveat, stated once:</b> suppression happens above the engine router, but only Kokoro
/// renders annotations — Piper has no markup mechanism and drops <c>TtsRenderContext.Rules</c>
/// entirely (SPEC F96.3). A render that falls over onto Piper therefore gets NEITHER the suppressed
/// correction NOR the rule for that word: engine-default pronunciation in an already-degraded mode.
/// Ruled acceptable (gh-#491): fallback is temporary degradation (gh-#404/T148 make it opt-in), the
/// rules editor is the curated system, and re-applying a suppressed correction per-engine would put
/// engine knowledge above the router the F70.4 layering exists to keep out.
/// </para>
///
/// Both faces of the invariant live HERE — the render-time filter
/// (<see cref="SuppressFor"/>) and the authoring-time collision scan (<see cref="CollidingWith"/>) —
/// mirroring <see cref="PersonaOverStationMerge"/>'s shape one invariant over: one type states the
/// precedence once, so the render path and the rules API can never disagree about who wins.
/// </summary>
public static class RuleOverCorrectionPrecedence
{
    /// <summary>The one collision predicate (see class remarks): identity equality between a
    /// correction's <see cref="SpeechCorrection.From"/> and a rule's
    /// <see cref="PronunciationRule.Pattern"/>, case-insensitive.</summary>
    public static bool Collides(string correctionFrom, string rulePattern) =>
        string.Equals(correctionFrom, rulePattern, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The render-time face: returns <paramref name="corrections"/> with every correction that
    /// collides with any of <paramref name="rules"/> removed, reporting the suppressed corrections'
    /// <see cref="SpeechCorrection.From"/> values (in set order, duplicates preserved — one log line
    /// per suppressed compiled rule) via <paramref name="suppressedFroms"/>. Returns the SAME
    /// instance untouched when nothing collides — the overwhelmingly common case allocates nothing.
    /// <paramref name="rules"/> is the resolved <c>TtsRenderContext.Rules</c> shape, so an
    /// audition's candidate layer suppresses exactly like a saved rule (audition/air parity,
    /// SPEC F126.1).
    /// </summary>
    public static SpeechCorrectionSet SuppressFor(
        SpeechCorrectionSet corrections,
        IReadOnlyList<ContextPronunciationRule> rules,
        out IReadOnlyList<string> suppressedFroms)
    {
        ArgumentNullException.ThrowIfNull(corrections);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            suppressedFroms = [];
            return corrections;
        }

        var result = corrections.Without(
            correction => rules.Any(rule => Collides(correction.From, rule.Pattern)),
            out var removed);
        suppressedFroms = [.. removed.Select(correction => correction.From)];
        return result;
    }

    /// <summary>
    /// The authoring-time face (the rules API's write warning): every compiled correction in
    /// <paramref name="corrections"/> that collides with <paramref name="rulePattern"/>, in set
    /// order. The caller words the warning; this only answers "which corrections would this rule
    /// suppress?" with the same predicate the render path enforces.
    /// </summary>
    public static IReadOnlyList<SpeechCorrection> CollidingWith(
        SpeechCorrectionSet corrections, string rulePattern)
    {
        ArgumentNullException.ThrowIfNull(corrections);

        return [.. corrections.Rules.Where(correction => Collides(correction.From, rulePattern))];
    }
}
