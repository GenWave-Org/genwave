using System.Text.RegularExpressions;
using GenWave.Tts;

namespace GenWave.VoiceOff.Text;

/// <summary>Builds the text an engine is handed for a gauntlet line, in the plain or the markup lane (SPEC F214.3).</summary>
internal static class CueText
{
    /// <summary>
    /// Plain lane: cue markers are stripped, then the station's normalizer runs, then (for
    /// <see cref="PlainLaneStyle.StationSpeechMarkup"/>) the shipped pronunciation and pause markup.
    /// </summary>
    internal static string Plain(
        string text, SpeechCorrectionSet corrections, PronunciationRuleSet rules, PlainLaneStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(corrections);
        ArgumentNullException.ThrowIfNull(rules);

        string normalized = SpeechText.Normalize(CueVocabulary.Strip(text), corrections);
        return style == PlainLaneStyle.StationSpeechMarkup
            ? KokoroSpeechMarkup.Render(normalized, rules, new TtsOptions().SentencePauseSeconds)
            : normalized;
    }

    /// <summary>
    /// Markup lane: the text between cues is normalized (the normalizer would otherwise flatten the
    /// braces), and each cue becomes the engine profile's native tag. A cue the map sends to null, or
    /// does not list, is stripped.
    /// </summary>
    internal static string Markup(
        string text, SpeechCorrectionSet corrections, IReadOnlyDictionary<string, string?> cueMap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(corrections);
        ArgumentNullException.ThrowIfNull(cueMap);

        List<string> parts = [];
        int cursor = 0;
        foreach (Match token in CueVocabulary.Token().Matches(text))
        {
            AddNormalized(parts, text[cursor..token.Index], corrections);
            if (cueMap.TryGetValue(token.Groups[1].Value, out string? tag) && !string.IsNullOrEmpty(tag))
            {
                parts.Add(tag);
            }

            cursor = token.Index + token.Length;
        }

        AddNormalized(parts, text[cursor..], corrections);
        return string.Join(' ', parts);
    }

    private static void AddNormalized(List<string> parts, string segment, SpeechCorrectionSet corrections)
    {
        string normalized = SpeechText.Normalize(segment, corrections);
        if (normalized.Length > 0)
        {
            parts.Add(normalized);
        }
    }
}
