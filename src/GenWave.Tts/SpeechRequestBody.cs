namespace GenWave.Tts;

using System.Text.Json;
using GenWave.Core.Domain;

/// <summary>
/// The JSON body of <c>POST /v1/audio/speech</c> (SPEC F219.4), shaped by the engine's
/// <see cref="VoiceEngineProfile"/>: <c>speed</c> is present only when the profile has speed, and
/// Kokoro IPA / <c>[pause:Ns]</c> markup is applied only when the profile says so. Applied here,
/// below the normalizer chokepoint, so upstream cache keys never see markup. The single place the
/// body is built; <see cref="SpeechEngineClient"/> is its only caller.
/// </summary>
static class SpeechRequestBody
{
    public static string Build(
        VoiceEngineProfile profile, string text, string voice, string format,
        PronunciationRuleSet rules, double sentencePauseSeconds, double pace,
        out IReadOnlyList<PronunciationMatch> matches)
    {
        var input = text;
        matches = [];
        if (profile.Ipa || profile.PauseTag)
        {
            input = KokoroSpeechMarkup.Render(
                text,
                profile.Ipa ? rules : PronunciationRuleSet.Empty,
                profile.PauseTag ? sentencePauseSeconds : 0,
                out matches);
        }

        // Field order is part of the wire shape the Kokoro specs pin; speed is absent, never null.
        return profile.Speed
            ? JsonSerializer.Serialize(new { input, voice, response_format = format, speed = pace })
            : JsonSerializer.Serialize(new { input, voice, response_format = format });
    }
}
