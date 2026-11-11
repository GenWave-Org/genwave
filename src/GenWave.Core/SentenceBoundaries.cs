using System.Text.RegularExpressions;

namespace GenWave.Core;

/// <summary>
/// The one sentence-boundary rule (SPEC F123.2, F218.35): a terminator is a boundary only when whitespace or the
/// end of the text follows (after any closing quote), and a <c>.</c> does not count when the word before it is a
/// known abbreviation ("Dr.", "St.") or a lone initial ("B.B.", "U.S."). Shared by <c>LlmCopyWriter</c> and
/// <c>SentenceTrimmer</c> so the abbreviation list lives in exactly one place.
/// </summary>
public static partial class SentenceBoundaries
{
    // A terminator, one optional closing quote mark, then whitespace or end-of-text.
    [GeneratedRegex(@"[.!?][""'”’]?(?=\s|$)")]
    private static partial Regex Plain();

    // The trimmer's wider rule: "…" also terminates, and any run of closing quotes/brackets stays with its sentence.
    [GeneratedRegex(@"[.!?…][""'”’)]*(?=\s|$)")]
    private static partial Regex Spoken();

    // Abbreviations whose trailing period is never a sentence's end (F123.2 review finding): "St. Vincent", "Dr. Dre".
    // Deliberately short and curated; the bias is toward over-rejecting a boundary.
    static readonly HashSet<string> KnownAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dr", "Mr", "Mrs", "Ms", "St", "Mt", "Sgt", "Jr", "Sr", "Ft", "Bros", "vs", "etc", "approx", "feat",
    };

    /// <summary>
    /// The exclusive end index of every real sentence boundary in <paramref name="text"/>, in order (the end includes
    /// the terminator and its closing quote). <paramref name="spoken"/> widens the rule for the trimmer: "…" terminates
    /// and a run of closing quotes or brackets stays with its sentence.
    /// </summary>
    public static IEnumerable<int> CutPoints(string text, bool spoken = false)
    {
        foreach (Match match in (spoken ? Spoken() : Plain()).Matches(text))
            if (!IsAbbreviationBoundary(text, match.Index))
                yield return match.Index + match.Length;
    }

    /// <summary>
    /// True when the terminator at <paramref name="terminatorIndex"/> is an abbreviation's or a lone initial's period
    /// (<c>!</c>/<c>?</c> never are). The word before the period is the maximal run of letters scanning backward; a
    /// lone initial is a single letter not preceded by an apostrophe (so a contraction's final letter, "don't.", is not
    /// misread as an initial). "U.S." falls out of the same check: the character before the "S" is the inner period.
    /// </summary>
    static bool IsAbbreviationBoundary(string text, int terminatorIndex)
    {
        if (text[terminatorIndex] != '.')
            return false;

        var wordStart = terminatorIndex;
        while (wordStart > 0 && char.IsLetter(text[wordStart - 1]))
            wordStart--;

        if (wordStart == terminatorIndex)
            return false;

        var word = text[wordStart..terminatorIndex];
        if (KnownAbbreviations.Contains(word))
            return true;

        if (word.Length != 1)
            return false;

        var beforeWordIndex = wordStart - 1;
        return beforeWordIndex < 0 || !IsApostrophe(text[beforeWordIndex]);
    }

    static bool IsApostrophe(char c) => c is '\'' or '’';
}
