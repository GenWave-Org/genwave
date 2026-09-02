namespace GenWave.Ads;

/// <summary>
/// Word-boundary match of a folded entry list against already-folded script text (SPEC F160.3) — the
/// one comparison both <see cref="AdBrandBlocklist"/> and <see cref="AdProfanityList"/> run.
///
/// <para>
/// <b>Padded-<see cref="string.Contains(string, StringComparison)"/>, never regex</b> (PLAN T399
/// review N1): <see cref="AdCopyFold"/> guarantees folded text contains ONLY lowercase ASCII
/// letters/digits and single spaces — no other punctuation survives the fold. In that restricted
/// alphabet, padding both the haystack and the needle with one leading/trailing space and doing a
/// plain ordinal substring search is EXACTLY equivalent to a <c>\bENTRY\b</c> regex match (a space is
/// the only possible word-boundary character left), without per-call regex construction over what
/// can be a couple hundred entries.
/// </para>
/// </summary>
internal static class FoldedWordListMatcher
{
    /// <summary>The first entry (in list order) that appears as a whole word/phrase in ANY of
    /// <paramref name="foldedScriptVariants"/> (see <see cref="AdCopyFold.FoldVariants"/>), or
    /// <see langword="null"/> when none do.</summary>
    public static string? FirstMatch(IReadOnlyList<string> foldedScriptVariants, IReadOnlyList<string> foldedEntries)
    {
        foreach (var variant in foldedScriptVariants)
        {
            var paddedVariant = $" {variant} ";
            foreach (var entry in foldedEntries)
            {
                if (entry.Length == 0)
                    continue;

                if (paddedVariant.Contains($" {entry} ", StringComparison.Ordinal))
                    return entry;
            }
        }

        return null;
    }
}
