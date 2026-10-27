using GenWave.Tts;

namespace GenWave.VoiceOff.Text;

/// <summary>
/// What a fresh install of the station feeds Kokoro: the one place the as-shipped card's text settings live.
/// Nothing here is chosen for the listening; each value is the station's own default.
/// </summary>
internal static class AsShippedStation
{
    /// <summary>
    /// Operator corrections on a fresh install: none. The <c>Tts:Corrections</c> setting defaults to "" in
    /// <c>src/GenWave.Host/Configuration/StationSettingsAllowlist.cs:258</c>, and <c>TtsCorrectionsOptions.Corrections</c>
    /// (<c>src/GenWave.Tts/TtsCorrectionsOptions.cs</c>) documents blank as <see cref="SpeechCorrectionSet.Empty"/>.
    /// </summary>
    internal static SpeechCorrectionSet Corrections => SpeechCorrectionSet.Empty;

    /// <summary>
    /// Pronunciation rules on a fresh install: none. <c>Tts:Pronunciations</c> defaults to "" in
    /// <c>src/GenWave.Host/Configuration/StationSettingsAllowlist.cs:266</c>; no persona card is applied here.
    /// </summary>
    internal static PronunciationRuleSet Rules => PronunciationRuleSet.Empty;

    /// <summary>
    /// The sentence pause the Kokoro path appends: <c>TtsOptions.SentencePauseSeconds</c>, default 0.6
    /// (<c>src/GenWave.Tts/TtsOptions.cs:33</c>); appsettings.json sets only <c>Tts:BlurbRetentionHours</c>.
    /// </summary>
    internal static double SentencePauseSeconds => new TtsOptions().SentencePauseSeconds;
}
