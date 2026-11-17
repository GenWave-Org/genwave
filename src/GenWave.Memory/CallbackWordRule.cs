namespace GenWave.Memory;

/// <summary>
/// The callback word rule (SPEC F218.45, pure): a line uses a memory when it shares at least one distinctive word
/// with it. Distinctive = four or more letters (digits do not count) as written in the memory, not a stop word, not excluded.
/// Words are lowercased by <see cref="RepetitionText.Tokens"/> and compared by <see cref="CallbackWordForms"/>.
/// </summary>
public static class CallbackWordRule
{
    internal const int MinLetters = 4;

    public static CallbackMatch Used(string memory, string line, CallbackExclusions exclusions)
    {
        var lineBases = CallbackWordForms.BasesOfAll(RepetitionText.Tokens(line));
        var shared = DistinctiveWords(memory, exclusions)
            .Where(word => CallbackWordForms.Bases(word).Overlaps(lineBases))
            .ToList();
        return new CallbackMatch(shared.Count > 0, shared);
    }

    /// <summary>False when the memory has no distinctive word: it can never be eligible.</summary>
    public static bool HasDistinctiveWord(string memory, CallbackExclusions exclusions) =>
        DistinctiveWords(memory, exclusions).Any();

    static IEnumerable<string> DistinctiveWords(string memory, CallbackExclusions exclusions) =>
        RepetitionText.Tokens(memory)
            .Where(word => word.Length >= MinLetters && word.All(char.IsLetter) && !RepetitionCheck.StopWords.Contains(word) && !exclusions.Contains(word))
            .Distinct(StringComparer.Ordinal);
}
