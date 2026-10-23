using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>The text an engine is handed for a piece of gauntlet text (SPEC F214.3). Every path runs through the station's normalizer first.</summary>
internal static class ClipTextBuilder
{
    internal static string Build(string text, RenderLane lane, EngineProfile profile, GauntletSet set)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(set);
        return lane switch
        {
            RenderLane.Plain => CueText.Plain(
                text, set.Corrections, set.Pronunciations,
                profile.Baseline ? PlainLaneStyle.StationSpeechMarkup : PlainLaneStyle.Bare),
            RenderLane.Markup => CueText.Markup(text, set.Corrections, profile.Markup),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unknown lane."),
        };
    }
}
