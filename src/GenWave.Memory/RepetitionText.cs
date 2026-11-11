using System.Text;
using System.Text.RegularExpressions;

namespace GenWave.Memory;

/// <summary>Normalisation and key-phrase extraction shared by <see cref="RepetitionCheck"/>.</summary>
static partial class RepetitionText
{
    // Double quotes (straight/curly), or a single-quoted span that may hold apostrophes: it opens at a word edge
    // and closes on the first quote not followed by a letter or digit, so "that's" never ends the span.
    [GeneratedRegex("""["“]([^"“”]+)["”]|(?<![\p{L}\p{N}])['‘](.+?)['’](?![\p{L}\p{N}])""")]
    private static partial Regex QuotedSpan();

    /// <summary>Lowercase, drop apostrophes, turn every other non-alphanumeric into a break, split.</summary>
    public static string[] Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is not ('\'' or '’'))
                sb.Append(' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Every quoted span of <paramref name="text"/>, trimmed, as written; blank spans dropped.</summary>
    public static IEnumerable<string> QuotedSpans(string text) =>
        QuotedSpan().Matches(text)
            .Select(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim())
            .Where(s => s.Length > 0);

    /// <summary>The rule's longest quoted span, else its last four words; trimmed, as written (an unclosed quote mark stays in).</summary>
    public static string KeyPhrase(string ruleText)
    {
        var quoted = QuotedSpans(ruleText).OrderByDescending(s => s.Length).FirstOrDefault();
        if (!string.IsNullOrEmpty(quoted))
            return quoted;

        var words = ruleText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Skip(Math.Max(0, words.Length - 4))).Trim().TrimEnd('.', '!', '?', ',', ';', ':');
    }
}
