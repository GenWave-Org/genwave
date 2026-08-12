namespace GenWave.Tts;

/// <summary>Which of the two SPEC F97.3 sources a merged pronunciation rule came from — the two
/// terms <see cref="PronunciationRuleSet.MergeWithProvenance"/> tags its projection with (T144's
/// rules API).</summary>
public enum PronunciationRuleSource
{
    Station,
    Persona,
}
