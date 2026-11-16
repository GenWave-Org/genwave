using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Reads the lore model's reply, an untrusted boundary (SPEC F218.20): one entry per line. A Markdown code fence,
/// bullets, numbering and wrapping quotes are tolerated. NUL characters are removed. Blank, over-length, preamble (ending in a colon) and repeated lines are dropped, as is
/// anything whose normalised text matches a line in <c>known</c>; at most <see cref="MaxLines"/> are kept.
/// </summary>
public static class LoreReplyParser
{
    /// <summary>Most entries one suggestion stores.</summary>
    public const int MaxLines = 5;

    public static IReadOnlyList<string> Parse(string reply, IEnumerable<string> known)
    {
        var seen = new HashSet<string>(known.Select(LifeMemoryRules.Normalise), StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (var raw in reply.Replace("\0", string.Empty, StringComparison.Ordinal).ReplaceLineEndings("\n").Split('\n'))
        {
            var line = Clean(raw);
            if (line.Length == 0 || line.Length > MemoryText.MaxChars || line.EndsWith(':') || line.StartsWith("```", StringComparison.Ordinal))
                continue;
            if (LifeMemoryRules.Normalise(line) is not { Length: > 0 } key || !seen.Add(key))
                continue;

            lines.Add(line);
            if (lines.Count == MaxLines)
                break;
        }

        return lines;
    }

    static string Clean(string raw)
    {
        var line = raw.Trim();
        var start = 0;
        while (start < line.Length && (line[start] is '-' or '*' or '•' || char.IsWhiteSpace(line[start])))
            start++;
        line = line[start..];

        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
            digits++;
        if (digits > 0 && digits + 1 < line.Length && line[digits] is '.' or ')' && char.IsWhiteSpace(line[digits + 1]))
            line = line[(digits + 1)..];

        return line.Trim().Trim('"', '“', '”').Trim();
    }
}
