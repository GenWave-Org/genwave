using System.Text;
using System.Text.RegularExpressions;

namespace GenWave.Core;

/// <summary>
/// The one word-tokenising rule the repetition machinery and the writer's "still names the track" check share
/// (SPEC F218.34/F218.35): both sides of every comparison go through <see cref="Of"/>, so a possessive never
/// separates "ABBA's" from "ABBA". Pure.
/// </summary>
public static partial class WordTokens
{
    // A possessive "'s" ends a word ("ABBA's Knowing Me" is the artist and the title); "we've" and "don't" stay whole.
    // The drop applies to every token (candidate, recent texts, names, quirk keys, the tagline ledger) so both sides
    // of a comparison agree (F218.34). Input is always lowercased first, so the pattern is lowercase-only.
    // Contractions keep their 's so "that's" stays apart from "thats" and "it's" still matches "its": the fixed list below.
    [GeneratedRegex("""(?<![\p{L}\p{N}])(?!(?:it|that|he|she|what|here|there|let|who|where)['’]s(?![\p{L}\p{N}]))(\p{L}+)['’]s(?![\p{L}\p{N}])""")]
    private static partial Regex Possessive();

    /// <summary>Lowercase, drop a possessive "'s", drop apostrophes, turn every other non-alphanumeric into a break, split.</summary>
    public static string[] Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var sb = new StringBuilder(text.Length);
        foreach (var c in Possessive().Replace(text.ToLowerInvariant(), "$1"))
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is not ('\'' or '’'))
                sb.Append(' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>True when <paramref name="phrase"/>'s tokens appear as one contiguous run in <paramref name="text"/>'s; a phrase with no tokens is never found.</summary>
    public static bool ContainsPhrase(string? text, string? phrase)
    {
        var needle = Of(phrase);
        if (needle.Length == 0)
            return false;
        return Of(text).AsSpan().IndexOf(needle.AsSpan()) >= 0;
    }
}
