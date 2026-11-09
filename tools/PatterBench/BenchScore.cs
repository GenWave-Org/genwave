namespace GenWave.PatterBench;

using System.Text;
using System.Text.RegularExpressions;
using GenWave.Core.Domain;

/// <summary>
/// The variety scores for one model and persona, as pure functions of the copies (SPEC F218.27).
/// Text is lowercased and stripped of punctuation first, the same idea as the app's repeat check (F218.11).
/// </summary>
internal static partial class BenchScore
{
    private const int GramSize = 4;
    private const int TopRepeatCount = 5;

    // Lockstep with src/GenWave.Memory/RepetitionText.cs (QuotedSpan, Tokens): this tool cannot reference
    // GenWave.Memory, so keep the quoting rule and apostrophe handling identical by hand (F218.11). KeyPhrase
    // is NOT an exact port: it picks the longest span by normalised length and takes the last four words after
    // normalising (the source uses trimmed raw length and raw space-split words). The shipped cards read the same.
    [GeneratedRegex("""["“]([^"“”]+)["”]|(?<![\p{L}\p{N}])['‘](.+?)['’](?![\p{L}\p{N}])""")]
    private static partial Regex QuotedSpanPattern();

    /// <summary>Lowercase, drop apostrophes, turn every other non-alphanumeric into a break (RepetitionText.Tokens).</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (c is not ('\'' or '’'))
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Distinct four-word grams over all grams, grams never crossing from one copy into the next. Null when no copy has four words.</summary>
    internal static double? DistinctFourGramRatio(IReadOnlyList<string> copies)
    {
        var grams = copies.SelectMany(FourGrams).ToList();
        return grams.Count == 0 ? null : (double)grams.Distinct(StringComparer.Ordinal).Count() / grams.Count;
    }

    /// <summary>The five four-word phrases found in the most copies (at least two), most first, ties in ordinal order.</summary>
    internal static IReadOnlyList<RepeatedPhrase> TopRepeats(IReadOnlyList<string> copies) =>
        copies
            .SelectMany(copy => FourGrams(copy).Distinct(StringComparer.Ordinal))
            .GroupBy(gram => gram, StringComparer.Ordinal)
            .Where(group => group.Count() >= 2)
            .Select(group => new RepeatedPhrase(group.Key, group.Count()))
            .OrderByDescending(phrase => phrase.Copies)
            .ThenBy(phrase => phrase.Phrase, StringComparer.Ordinal)
            .Take(TopRepeatCount)
            .ToList();

    /// <summary>The longest quoted span of the rule text, else its last four words (F218.11).</summary>
    internal static string KeyPhrase(string ruleText)
    {
        var quoted = QuotedSpanPattern().Matches(ruleText)
            .Select(m => Normalize(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
            .Where(span => span.Length > 0)
            .OrderByDescending(span => span.Length)
            .FirstOrDefault();
        return quoted ?? string.Join(' ', Normalize(ruleText).Split(' ').TakeLast(GramSize));
    }

    /// <summary>The share of copies that say any once-per-shift quirk's key phrase. Null when the persona has no such quirk.</summary>
    internal static double? CatchphraseRate(IReadOnlyList<string> copies, IReadOnlyList<QuirkRule>? rules)
    {
        var phrases = (rules ?? [])
            .Where(rule => rule.MaxPerShift is not null)
            .Select(rule => $" {KeyPhrase(rule.Text)} ")
            .ToList();
        if (phrases.Count == 0 || copies.Count == 0)
        {
            return null;
        }

        var hits = copies.Count(copy => phrases.Any(phrase => $" {Normalize(copy)} ".Contains(phrase, StringComparison.Ordinal)));
        return (double)hits / copies.Count;
    }

    /// <summary>Nearest-rank percentile of the latencies, in milliseconds. Null when there are none.</summary>
    internal static double? PercentileMs(IReadOnlyList<TimeSpan> latencies, int percentile)
    {
        if (latencies.Count == 0)
        {
            return null;
        }

        var sorted = latencies.Select(l => l.TotalMilliseconds).Order().ToList();
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    private static IEnumerable<string> FourGrams(string copy)
    {
        var words = Normalize(copy).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + GramSize <= words.Length; i++)
        {
            yield return string.Join(' ', words.Skip(i).Take(GramSize));
        }
    }
}
