namespace GenWave.Memory;

/// <summary>
/// Words that never make a repeat on their own (SPEC F218.11). Stop words match exactly; a non-stop name word also
/// matches with a trailing "s" or "ed" ("Duran's", "remastered"). No stemming beyond that.
/// Track facts are the title, artist and album words (and their forms) plus year facts; persona and station words
/// are plain exclusions. A year fact is a year digit token, or a year number word next to another year word or digit
/// ("nineteen fifty nine"); a lone number word ("this one is for you") is ordinary text.
/// </summary>
sealed class ExcludedWords(
    IReadOnlySet<string> stop,
    IReadOnlySet<string> otherNames,
    IReadOnlySet<string> trackNames,
    IReadOnlySet<string> year)
{
    /// <summary>Per token of the candidate: is it a year fact (digits alone, number words only beside another year token).</summary>
    public bool[] YearFacts(string[] tokens)
    {
        var facts = new bool[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!year.Contains(tokens[i]))
                continue;
            facts[i] = IsDigits(tokens[i]) ||
                (i > 0 && year.Contains(tokens[i - 1])) ||
                (i + 1 < tokens.Length && year.Contains(tokens[i + 1]));
        }
        return facts;
    }

    /// <summary>True if the token is not a year fact, stop word, or name word or form.</summary>
    public bool IsFresh(string token, bool isYearFact) =>
        !isYearFact && !stop.Contains(token) && !IsName(otherNames, token) && !IsName(trackNames, token);

    /// <summary>True if the token is a year fact or a title, artist or album word (or a form of one).</summary>
    public bool IsTrackFact(string token, bool isYearFact) =>
        isYearFact || IsName(trackNames, token);

    static bool IsDigits(string token) => token.All(char.IsAsciiDigit);

    static bool IsName(IReadOnlySet<string> names, string token) =>
        names.Contains(token) || IsNameForm(names, token);

    static bool IsNameForm(IReadOnlySet<string> names, string token) =>
        (token.EndsWith('s') && names.Contains(token[..^1])) ||
        (token.EndsWith("ed", StringComparison.Ordinal) && names.Contains(token[..^2]));
}
