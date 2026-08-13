namespace GenWave.Tts;

using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

/// <summary>
/// THE one public resolve seam that turns station∪persona pronunciation rules — plus an optional
/// unsaved candidate layered on top — into the <c>TtsRenderContext.Rules</c> shape (SPEC F97.3,
/// F97.4, F126.1; T274 review finding F3). <see cref="TtsSegmentSource"/> (the on-air render) and
/// <c>TtsPreviewController</c> (the audition) both call <see cref="ResolveForRender"/> rather than
/// each re-deriving their own merge — audition/air parity is a property of ONE implementation, not
/// a coincidence two call sites happen to agree on today.
///
/// <para>
/// Deliberately NOT <see cref="PronunciationRuleSet.MergeWithProvenance"/>: that projection exists
/// for DISPLAY (<c>GET /api/pronunciations</c>) — its <see cref="MergedPronunciationRule.Source"/>
/// tag names which of its TWO ARGUMENTS supplied a rule, which becomes actively misleading once a
/// THIRD layer (a preview's candidate) enters the picture standing in for one of those arguments,
/// and reusing it for a real render made its own "never used for matching" doc claim false (T274
/// review finding F3, since reverted — see that method's own remarks). This resolver calls
/// <see cref="PronunciationRuleSet.Merge"/> directly, the matching-purposed API, the same one
/// <see cref="PronunciationRuleProvider.BuildMerged"/> already uses for the station∪persona half.
/// </para>
/// </summary>
public static class PronunciationRuleResolver
{
    /// <summary>
    /// Resolves the station∪persona merge (SPEC F97.3, F97.4) via
    /// <see cref="PronunciationRuleProvider.BuildMerged"/>, then layers <paramref name="candidates"/>
    /// OVER that merge when supplied (SPEC F126.1, STORY-323 AC2): a candidate wins any overlap AND
    /// any identical (Pattern, Word) identity against the resolved merge, through the exact SAME
    /// persona-over-station precedence mechanism (<see cref="PronunciationRuleSet.Merge"/>) the
    /// station∪persona merge one layer down already uses — reusing it rather than inventing a
    /// second overlap policy. <paramref name="candidates"/> is assumed already validated by the
    /// caller (<see cref="PronunciationRuleValidator"/>), so <see cref="PronunciationRuleSet.Create"/>
    /// compiles every entry cleanly; <see langword="null"/> or empty means "no candidate layer" —
    /// the plain station∪persona merge, byte-identical to every caller that predates STORY-323.
    ///
    /// Returns the <see cref="ContextPronunciationRule"/> shape <c>TtsRenderContext.Rules</c>
    /// carries — the ONE conversion site from the compiled <see cref="PronunciationRuleSet"/> shape
    /// to that mirrored, dependency-free contract type (formerly duplicated as a private helper on
    /// <see cref="TtsSegmentSource"/>; T274 review finding F3 consolidated it here).
    /// </summary>
    public static IReadOnlyList<ContextPronunciationRule> ResolveForRender(
        PronunciationRuleSet station, IReadOnlyList<PronunciationRule> cardRules,
        IReadOnlyList<PronunciationRule>? candidates = null)
    {
        var merged = PronunciationRuleProvider.BuildMerged(station, cardRules);

        if (candidates is { Count: > 0 })
            merged = PronunciationRuleSet.Merge(merged, PronunciationRuleSet.Create(candidates));

        return ToContextRules(merged);
    }

    // The resolved-rule shape riding on TtsRenderContext.Rules (GenWave.Core.Domain.PronunciationRule)
    // is not the same type PronunciationRuleSet compiles — see that mirror type's own remarks for
    // why GenWave.Core.Domain (the zero-dependency contract project) cannot share it with
    // GenWave.Tts. The opposite direction is PronunciationRuleSet.FromContext, the single seam both
    // Kokoro request builders share (T137 consolidated two duplicate copies into it).
    static IReadOnlyList<ContextPronunciationRule> ToContextRules(PronunciationRuleSet rules) =>
        [.. rules.Rules.Select(rule => new ContextPronunciationRule(rule.Pattern, rule.Word, rule.Ipa))];
}
