using System.Text.RegularExpressions;
using GenWave.VoiceOff.Deck;
using GenWave.Tts;

namespace GenWave.VoiceOff.Text;

/// <summary>
/// Turns the script into the text one card's engine is handed (SPEC F217.3). Pure: no IO.
/// A best-effort card gets Dean's paragraph verbatim with each cue replaced by the card's tag or written
/// stand-in (or removed when the card maps it to null or does not list it), unless the card overrides the
/// paragraph, in which case the override is the text. The as-shipped card gets the station's own path:
/// cues dropped the same way a null mapping is, <see cref="SpeechText.Normalize"/>, then <see cref="KokoroSpeechMarkup"/>.
/// </summary>
internal static partial class CardText
{
    [GeneratedRegex(@"(\s*)" + CueVocabulary.TokenPattern + @"(\s*)")]
    private static partial Regex CueSite();

    private static readonly IReadOnlyDictionary<string, string?> NoCues = new Dictionary<string, string?>();

    internal static IReadOnlyList<ParagraphText> Prepare(Card card, ScriptFile script)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(script);
        return script.Paragraphs.Select(p => Prepare(card, p)).ToList();
    }

    internal static ParagraphText Prepare(Card card, ScriptParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(paragraph);
        return card.AsShipped ? AsShipped(paragraph) : BestEffort(card, paragraph);
    }

    // As-shipped ignores overrides: it is the reference, and the station has no such thing.
    private static ParagraphText AsShipped(ScriptParagraph paragraph)
    {
        string normalised = SpeechText.Normalize(ReplaceCues(paragraph.Text, NoCues), AsShippedStation.Corrections);
        string text = KokoroSpeechMarkup.Render(normalised, AsShippedStation.Rules, AsShippedStation.SentencePauseSeconds);
        return new ParagraphText(paragraph.Number, text, normalised, CueNames(paragraph.Text));
    }

    private static ParagraphText BestEffort(Card card, ScriptParagraph paragraph)
    {
        string expected = ReplaceCues(paragraph.Text, card.Cues);
        string text = card.Overrides.TryGetValue(paragraph.Number, out string? overridden) ? overridden : expected;
        IReadOnlyList<string> dropped = CueNames(paragraph.Text)
            .Where(cue => !card.Cues.TryGetValue(cue, out string? value) || value is null)
            .ToList();
        return new ParagraphText(paragraph.Number, text, expected, dropped);
    }

    /// <summary>The paragraph with each cue replaced by its mapping, or dropped when unmapped or null.</summary>
    internal static string ReplaceCues(string text, IReadOnlyDictionary<string, string?> cues) =>
        CueSite().Replace(text, site =>
        {
            string left = site.Groups[1].Value;
            string right = site.Groups[3].Value;
            if (cues.TryGetValue(site.Groups[2].Value, out string? replacement) && replacement is not null)
            {
                return left + replacement + right;
            }

            // Dropped: keep one space only when the cue sat between two words.
            return left.Length > 0 && right.Length > 0 ? " " : string.Empty;
        });

    private static List<string> CueNames(string text) =>
        CueVocabulary.Token().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
}
