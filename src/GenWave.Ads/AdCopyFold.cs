using System.Globalization;
using System.Text;

namespace GenWave.Ads;

/// <summary>
/// The fold every blocklist/profanity-list comparison in <see cref="AdScriptValidator"/> runs (SPEC
/// F160.3's "Coka-Cola presents" check) — case, leet-speak, and separator-punctuation collapsed to a
/// canonical space-joined token stream, so <c>"Coca-Cola"</c>, <c>"C0ca C0la"</c>, and (via the
/// spaced-letter merge below) <c>"c o c a   c o l a"</c> all fold toward the SAME <c>"coca cola"</c>.
///
/// <para>
/// <b>Never applied to phone-shape detection</b> (<see cref="PhoneShapeCheck"/> reads the RAW script
/// text): the leet map below turns digits INTO letters, which would destroy the very digit runs that
/// check exists to find.
/// </para>
///
/// <para>
/// <b>Two entry points, two trust levels</b> (PLAN T399 review F2/F3): <see cref="Fold"/> is the
/// single canonical form used to fold OUR OWN data (<c>BrandBlocklist.txt</c>/<c>ProfanityList.txt</c>
/// entries) — those are curated, never adversarial, so one deterministic shape is enough. <see
/// cref="FoldVariants"/> is used on the UNTRUSTED script text being validated: it returns several
/// candidate foldings, because a genuine single-letter word (an article like "a") sitting next to a
/// letter-spaced evasion attempt is ambiguous about where the "spaced-out word" actually starts or
/// ends — see <see cref="MergeRun"/>'s own remarks.
/// </para>
///
/// <para>
/// <b>What this fold does NOT catch</b> (documented gap, not a promise): true Unicode confusables —
/// a Cyrillic "с" (U+0441) standing in for Latin "c", or fullwidth forms ("ｃｏｋｅ") — are not
/// normalized to their Latin look-alikes. <see cref="char.IsAsciiLetterOrDigit(char)"/> only
/// recognizes ASCII, so a confusable character is treated as a separator (breaking the word), not
/// folded. Full homograph normalization is a much larger undertaking (IDN-style confusable tables)
/// than this cheap, honest validator takes on.
/// </para>
/// </summary>
internal static class AdCopyFold
{
    /// <summary>The one canonical fold for TRUSTED data (blocklist/profanity file entries) — whole
    /// single-character runs merged, no alternate variants.</summary>
    public static string Fold(string text) => Join(MergeRuns(TokenizeAndFold(text), dropLeading: false, dropTrailing: false));

    /// <summary>
    /// Every candidate folding of UNTRUSTED script text worth matching against (PLAN T399 review F2):
    /// the whole-run merge, plus — because a stray single-letter word can sit directly against a
    /// letter-spaced evasion attempt on either side — a merge with the run's leading token held out,
    /// and one with its trailing token held out. Deduplicated; a script with no ambiguous run at all
    /// yields exactly one variant.
    /// </summary>
    public static IReadOnlyList<string> FoldVariants(string text)
    {
        var tokens = TokenizeAndFold(text);
        return new[]
        {
            Join(MergeRuns(tokens, dropLeading: false, dropTrailing: false)),
            Join(MergeRuns(tokens, dropLeading: true, dropTrailing: false)),
            Join(MergeRuns(tokens, dropLeading: false, dropTrailing: true)),
        }.Distinct(StringComparer.Ordinal).ToList();
    }

    static string Join(List<string> tokens) => string.Join(' ', tokens);

    /// <summary>
    /// Two passes, deliberately never one (PLAN T399 review F3): pass one splits <paramref
    /// name="text"/> into raw (unfolded) tokens on every character that is NOT a letter, digit, or a
    /// leet symbol (<c>$</c>/<c>@</c>) — an apostrophe or a Unicode <c>Cf</c> (Format — zero-width
    /// joiners and the like) character is consumed silently, neither a token character nor a
    /// separator. Pass two folds each raw token as a WHOLE only if it already contains a real ASCII
    /// letter; a token with no letter at all (a bare number, a price fragment) is left exactly as
    /// extracted, never leet-substituted. Without this split, a lone digit run like <c>"5 5"</c> would
    /// leet-fold to <c>"s s"</c> — an all-digit token has no word context to justify the substitution.
    /// </summary>
    static List<string> TokenizeAndFold(string text) => RawTokenize(text).Select(FoldToken).ToList();

    static List<string> RawTokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in text)
        {
            if (ch == '\'' || IsUnicodeFormatChar(ch))
                continue; // Invisible/joining characters fold away entirely — never kept, never a separator.

            if (IsTokenChar(ch))
            {
                current.Append(ch);
                continue;
            }

            FlushToken(tokens, current);
        }

        FlushToken(tokens, current);
        return tokens;
    }

    static void FlushToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
            return;

        tokens.Add(current.ToString());
        current.Clear();
    }

    static bool IsTokenChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '$' or '@';

    static bool IsUnicodeFormatChar(char ch) => CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format;

    static string FoldToken(string rawToken)
    {
        if (!rawToken.Any(char.IsAsciiLetter))
            return rawToken; // Pure digits/symbols never leet-substitute (PLAN T399 review F3).

        var builder = new StringBuilder(rawToken.Length);
        foreach (var ch in rawToken)
            builder.Append(LeetFoldChar(ch));

        return builder.ToString();
    }

    /// <summary>
    /// The leet-speak substitution table (SPEC F160.3): <c>0→o</c>, <c>1→l</c>, <c>3→e</c>, <c>4→a</c>,
    /// <c>5→s</c>, <c>7→t</c>, <c>$→s</c>, <c>@→a</c>. <c>1</c> could stand for either "l" or "i" in
    /// real leet usage; this table picks "l" as the single canonical fold — both sides of a comparison
    /// go through this SAME table. Only called on a token <see cref="FoldToken"/> already confirmed
    /// contains a real letter, so every OTHER digit (2, 6, 8, 9) and every ordinary ASCII letter simply
    /// lowercase-folds via the <see langword="default"/> arm.
    /// </summary>
    static char LeetFoldChar(char ch) => ch switch
    {
        '0' => 'o',
        '1' => 'l',
        '3' => 'e',
        '4' => 'a',
        '5' => 's',
        '7' => 't',
        '$' => 's',
        '@' => 'a',
        _ => char.ToLowerInvariant(ch),
    };

    /// <summary>Fewest consecutive single-character tokens treated as spacing evasion at all — a lone
    /// stray letter/digit next to ordinary words is never itself suspicious.</summary>
    const int MinSpacedRunLength = 2;

    static List<string> MergeRuns(List<string> tokens, bool dropLeading, bool dropTrailing)
    {
        var result = new List<string>();
        var i = 0;

        while (i < tokens.Count)
        {
            if (tokens[i].Length != 1)
            {
                result.Add(tokens[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < tokens.Count && tokens[i].Length == 1)
                i++;

            result.AddRange(MergeRun(tokens.GetRange(start, i - start), dropLeading, dropTrailing));
        }

        return result;
    }

    /// <summary>
    /// Merges one maximal run of consecutive single-character tokens (PLAN T399 review F2, round-2
    /// review R2-A). A run shorter than <see cref="MinSpacedRunLength"/> is left untouched. Otherwise:
    /// the WHOLE run glues into one token (<c>"c o k e"</c> -&gt; <c>"coke"</c>) — UNLESS <paramref
    /// name="dropLeading"/>/<paramref name="dropTrailing"/> asks to hold out the run's first/last
    /// token instead, so a genuine single-letter word sitting directly against the evasion attempt
    /// (<c>"a c o k e"</c> -&gt; the article "a" plus <c>"coke"</c>) doesn't corrupt the merge into a
    /// blob that matches nothing (<c>"acoke"</c>). The drop variants apply whenever the run has AT
    /// LEAST <see cref="MinSpacedRunLength"/> tokens (round-2 review R2-A correction — a run of
    /// EXACTLY 2 still needs its own "kept apart" variant: <c>"a M&amp;M's"</c>'s run is just
    /// <c>["a","m"]</c>, and gating the drop on a STRICTLY-longer run left that exact two-token case
    /// always falling through to the whole-merge branch regardless of which variant was asked for,
    /// so "Grab a M&amp;M's."/"A 5 hour energy for the road." — correctly-spelled brands, not evasion
    /// attempts — silently accepted). For a 2-token run, dropping one token leaves a single token
    /// behind, which <see cref="string.Concat(IEnumerable{string})"/> passes through unchanged — so
    /// the drop variant for a 2-run is simply "don't merge them", exactly the shape needed.
    /// </summary>
    static List<string> MergeRun(List<string> run, bool dropLeading, bool dropTrailing)
    {
        if (run.Count < MinSpacedRunLength)
            return run;

        if (dropLeading && run.Count >= MinSpacedRunLength)
            return [run[0], string.Concat(run.Skip(1))];

        if (dropTrailing && run.Count >= MinSpacedRunLength)
            return [string.Concat(run.Take(run.Count - 1)), run[^1]];

        return [string.Concat(run)];
    }
}
