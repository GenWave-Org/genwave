namespace GenWave.Memory;

/// <summary>
/// Word equality for the callback word rule (SPEC F218.45). Two words are the same when they share a base: the word
/// itself, or the word minus one trailing "s", "ed" or "ing", with a dropped "e" restored for "ed"/"ing"
/// ("restoring" -> "restore", "restores" -> "restore"). Forms are only ever <see cref="CallbackWordRule.MinLetters"/>
/// or more letters, the same four as a distinctive word, so "thing"/"the", "noted"/"not" and "news"/"new" stay apart
/// (accepted cost: "dogs"/"dog" and "used"/"use" do not match). "ss" is never an "s" plural ("class").
/// Words are never trimmed to a bare stem, so "here"/"her", "note"/"not" and "made"/"mad" stay apart.
/// Known spec-literal merges, left for the T732 replay to measure: "evening"/"even", "staring"/"star".
/// Differs from <c>ExcludedWords.IsNameForm</c> (repeats), which accepts "s"/"ed" only. T732 mirrors this list in SQL.
/// </summary>
static class CallbackWordForms
{
    /// <summary>Every base of every word, for testing one word against many at once.</summary>
    public static HashSet<string> BasesOfAll(IEnumerable<string> words)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in words)
            all.UnionWith(Bases(word));
        return all;
    }

    public static HashSet<string> Bases(string word)
    {
        var bases = new HashSet<string>(StringComparer.Ordinal) { word };
        if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal))
            AddBase(bases, word[..^1]);
        if (word.EndsWith("ed", StringComparison.Ordinal))
            AddBaseAndRestoredE(bases, word[..^2]);
        if (word.EndsWith("ing", StringComparison.Ordinal))
            AddBaseAndRestoredE(bases, word[..^3]);
        return bases;
    }

    static void AddBaseAndRestoredE(HashSet<string> bases, string stem)
    {
        AddBase(bases, stem);
        AddBase(bases, stem + "e");
    }

    static void AddBase(HashSet<string> bases, string candidate)
    {
        if (candidate.Length >= CallbackWordRule.MinLetters)
            bases.Add(candidate);
    }
}
