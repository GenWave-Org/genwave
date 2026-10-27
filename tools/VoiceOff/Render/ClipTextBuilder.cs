using GenWave.VoiceOff.Text;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>The text an engine is handed for a piece of gauntlet text (SPEC F214.3). Every path runs through the station's normalizer first.</summary>
internal static class ClipTextBuilder
{
    internal static string Build(string text, EngineProfile profile, GauntletSet set)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(set);
        return CueText.Plain(
            text, set.Corrections, set.Pronunciations,
            profile.Baseline ? PlainLaneStyle.StationSpeechMarkup : PlainLaneStyle.Bare);
    }
}
