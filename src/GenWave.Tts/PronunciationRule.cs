namespace GenWave.Tts;

/// <summary>
/// One pronunciation rule (SPEC F97.1): <see cref="Pattern"/> is the literal, word-boundary-
/// anchored phrase a rule must match; <see cref="Word"/> is the token *within* that match to
/// re-pronounce; <see cref="Ipa"/> is the phoneme string a supporting engine renders in its
/// place. The <see cref="Pattern"/>/<see cref="Word"/> split is what makes heteronyms
/// expressible (F97.2): "wind" in "wind down" and "wind" in "the wind" are the same spelling
/// with different phonemes, so a rule keyed on the word alone cannot disambiguate them — rules
/// are operator/persona-card data, matched deterministically; GenWave never infers part of
/// speech.
///
/// <para>
/// Compiled and matched by <see cref="PronunciationRuleSet"/>; this record only carries the raw
/// data. Rules come from two sources merged elsewhere (F97.3): station settings
/// (<c>Tts:Pronunciations</c>) and the active persona's card.
/// </para>
/// </summary>
public sealed record PronunciationRule(string Pattern, string Word, string Ipa)
{
    /// <summary>
    /// Builds a rule from operator/card input, defaulting <see cref="Word"/> to
    /// <paramref name="pattern"/> when the caller supplies none — the always-mispronounced-name
    /// case (<c>MacLeod</c>) needs no surrounding context to disambiguate (F97.1).
    /// </summary>
    public static PronunciationRule Parse(string pattern, string? word, string ipa) =>
        new(pattern, string.IsNullOrWhiteSpace(word) ? pattern : word, ipa);
}
