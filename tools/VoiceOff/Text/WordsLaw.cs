using System.Text.RegularExpressions;
using GenWave.VoiceOff.Deck;

namespace GenWave.VoiceOff.Text;

/// <summary>
/// The words law (SPEC F217.4): the words sent to the engine are the script's words, nothing added,
/// nothing dropped, nothing changed. Pure: no IO.
/// <para>
/// A tag is a bracketed <c>[...]</c> or angle <c>&lt;...&gt;</c> token. A best-effort card may use only the
/// tags its cue map declares (exact text); the as-shipped card may carry Kokoro's own <c>[pause:Ns]</c> and
/// <c>[word](/ipa/)</c> markup, and nothing else (the shipped pronunciation rules are empty today, so the
/// IPA unwrap only matters once they are not). Words are letter/digit runs, kept whole across an inner
/// apostrophe (<c>doesn't</c>), plus the spoken symbols (currency, <c>%&amp;@#+</c>), and compared
/// case-insensitively: capitals, punctuation, ellipses and <c>!?</c> are delivery, not words, so a card
/// may change them. A cue's stand-in words ("Um,") are
/// part of <see cref="ParagraphText.Expected"/> at the cue's position, so the real word "um" elsewhere is
/// never confused with one.
/// </para>
/// </summary>
internal static partial class WordsLaw
{
    [GeneratedRegex(@"\[[^\[\]]*\]|<[^<>\s][^<>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\[([^\[\]]+)\]\(/[^)]*/\)")]
    private static partial Regex IpaAnnotation();

    [GeneratedRegex(@"\A\[pause:\d+(?:\.\d+)?s\]\z")]
    private static partial Regex KokoroPause();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*|[\p{Sc}%&@#+]")]
    private static partial Regex Word();

    /// <summary>Every broken paragraph of <paramref name="paragraphs"/>; empty means the card passes.</summary>
    internal static IReadOnlyList<WordsLawFailure> Check(Card card, IReadOnlyList<ParagraphText> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(paragraphs);

        HashSet<string> declared = DeclaredTags(card);
        List<WordsLawFailure> failures = [];
        foreach (ParagraphText paragraph in paragraphs)
        {
            string engine = card.AsShipped ? IpaAnnotation().Replace(paragraph.Text, "$1") : paragraph.Text;
            string[] undeclared = Tag().Matches(engine).Select(m => m.Value)
                .Where(tag => !IsDeclared(card, declared, tag)).Distinct(StringComparer.Ordinal).ToArray();
            failures.AddRange(undeclared.Select(tag =>
                new WordsLawFailure(paragraph.Number, tag, $"Paragraph {paragraph.Number} uses the tag {tag}, which the card does not declare.")));

            WordsLawFailure? changed = FirstWordDifference(paragraph, engine);
            if (changed is not null)
            {
                failures.Add(changed);
            }
        }

        return failures;
    }

    private static HashSet<string> DeclaredTags(Card card) =>
        card.Cues.Values
            .Where(value => value is not null)
            .SelectMany(value => Tag().Matches(value ?? string.Empty).Select(m => m.Value))
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsDeclared(Card card, HashSet<string> declared, string tag) =>
        card.AsShipped ? KokoroPause().IsMatch(tag) : declared.Contains(tag);

    private static WordsLawFailure? FirstWordDifference(ParagraphText paragraph, string engine)
    {
        string[] expected = Words(paragraph.Expected);
        string[] actual = Words(engine);
        for (int i = 0; i < Math.Max(expected.Length, actual.Length); i++)
        {
            string want = i < expected.Length ? expected[i] : "(nothing)";
            string got = i < actual.Length ? actual[i] : "(nothing)";
            if (!string.Equals(want, got, StringComparison.Ordinal))
            {
                return new WordsLawFailure(
                    paragraph.Number,
                    null,
                    $"Paragraph {paragraph.Number} changes the script's words at word {i + 1}: expected \"{want}\", got \"{got}\".");
            }
        }

        return null;
    }

    private static string[] Words(string text) =>
        Word().Matches(Tag().Replace(text, " "))
            .Select(m => m.Value.ToLowerInvariant().Replace('’', '\''))
            .ToArray();
}
