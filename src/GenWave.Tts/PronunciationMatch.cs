namespace GenWave.Tts;

/// <summary>
/// One <see cref="PronunciationRuleSet.Match"/> hit: <see cref="Rule"/>'s
/// <see cref="PronunciationRule.Word"/> occupies the span <c>[Index, Index + Length)</c> in the
/// text that was searched — the specific occurrence a caller (the Kokoro adapter's markup
/// renderer, T133) annotates with <c>[word](/ipa/)</c>, never the whole matched
/// <see cref="PronunciationRule.Pattern"/>. Carries the whole rule rather than just its
/// <see cref="PronunciationRule.Ipa"/> so a caller can also log which rule fired (F97.5)
/// without a second lookup.
/// </summary>
public sealed record PronunciationMatch(int Index, int Length, PronunciationRule Rule);
