using System.Net;
using System.Text.RegularExpressions;

namespace GenWave.Tts;

/// <summary>
/// The single normalization chokepoint every booth-bound string passes through immediately before
/// TTS (SPEC F68). Pure and static: no I/O, no settings reads, zero non-BCL dependencies —
/// corrections are passed in by the caller. Passes run in a fixed order: think-strip, then
/// markdown strip, then HTML-entity decode, then operator corrections, then digit-anchored unit
/// expansion, then entity-safe <c>&amp;</c>-to-"and", then whitespace collapse (F68.2). Think-strip
/// runs first because nothing downstream should ever process leaked reasoning text; markdown runs
/// before entity decode so a bolded degree symbol still reaches the unit-expansion pass; operator
/// corrections run after cleanup (so a rule matches the readable text an operator sees in admin)
/// and before the built-in expansions (so a correction can pre-empt one); whitespace collapse runs
/// last to tidy whatever the earlier passes left behind.
/// </summary>
public static partial class SpeechText
{
    // Fixpoint cap for StripThinkBlocks (T28 review carry-forward) — deeper <think> nesting than
    // any real LLM output; guards the render path from a pathological input, never a real one.
    private const int MaxThinkNestingDepth = 64;

    /// <summary>Runs <paramref name="text"/> through every normalization pass in spec'd order.</summary>
    public static string Normalize(string text, SpeechCorrectionSet corrections)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(corrections);

        var decoded = PrepareForCorrections(text);
        var corrected = corrections.Apply(decoded, out _);
        var expanded = ExpandUnits(corrected);
        var withAnd = expanded.Replace("&", " and ");

        return CollapseWhitespace(withAnd);
    }

    /// <summary>
    /// Runs every normalization pass that precedes operator corrections — think-strip,
    /// markdown-strip, HTML-entity decode — the exact text <see cref="SpeechCorrectionSet.Apply"/>
    /// matches against inside <see cref="Normalize"/> (F68.2). Internal (same-assembly) rather than
    /// a second public overload of <see cref="Normalize"/>, so <see cref="NormalizingTtsSynthesizer"/>
    /// can determine which rules would actually fire for observability (SPEC F68.7) without
    /// re-deriving this pipeline or guessing from raw, pre-cleanup text — and without disturbing
    /// <see cref="Normalize"/>'s own signature/overload set (STORY-185's single-call-site guard).
    /// </summary>
    internal static string PrepareForCorrections(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var scrubbed = StripThinkBlocks(text);
        var withoutMarkdown = StripMarkdown(scrubbed);
        return WebUtility.HtmlDecode(withoutMarkdown);
    }

    private static string StripThinkBlocks(string text)
    {
        // ThinkBlockRx only ever matches an innermost pair (its content is guaranteed free of a
        // further nested "<think>"), so a single Replace resolves one level of nesting at a time.
        // Looping to a fixpoint peels nested blocks from the inside out until none remain — this
        // is what keeps a doubly-nested block from leaking its outer layer's text (F68.3).
        //
        // Capped at MaxThinkNestingDepth passes (T28 review carry-forward, wired live in T29): far
        // deeper nesting than any real LLM output produces, so this only ever bites a pathological
        // input. On cap-hit the loop falls through to the unclosed/orphan strips below rather than
        // spinning — the same "never stall the render path" discipline as SpeechCorrectionSet's own
        // 250ms per-rule match timeout.
        var stripped = text;
        string beforePass;
        var pass = 0;
        do
        {
            beforePass = stripped;
            stripped = ThinkBlockRx().Replace(stripped, string.Empty);
            pass++;
        }
        while (stripped != beforePass && pass < MaxThinkNestingDepth);

        // An unclosed <think> at end (no closing tag reached) is stripped conservatively to the
        // end of the string — nothing downstream should ever see partial reasoning text (F68.3).
        var withoutUnclosed = UnclosedThinkBlockRx().Replace(stripped, string.Empty);

        // Defensive final pass: a bare </think> with no opening tag at all (or the tag fragment
        // left once every properly nested pair above has already resolved) must never reach TTS
        // either (F68.3).
        return OrphanThinkCloseRx().Replace(withoutUnclosed, string.Empty);
    }

    private static string StripMarkdown(string text)
    {
        var withoutLinks = LinkRx().Replace(text, "$1");
        var withoutInlineCode = InlineCodeRx().Replace(withoutLinks, "$1");
        var withoutHeadings = HeadingRx().Replace(withoutInlineCode, string.Empty);
        var withoutBold = BoldRx().Replace(withoutHeadings, "$1");
        var withoutAsteriskItalic = ItalicAsteriskRx().Replace(withoutBold, "$1");

        // Underscore emphasis is anchored on word boundaries so snake_case survives untouched
        // (F68.4): the character immediately before/after an intraword underscore is itself a word
        // character, so \b never falls there.
        return ItalicUnderscoreRx().Replace(withoutAsteriskItalic, "$1");
    }

    private static string ExpandUnits(string text)
    {
        var withFahrenheit = DegreeFahrenheitRx().Replace(text, " degrees Fahrenheit");
        var withCelsius = DegreeCelsiusRx().Replace(withFahrenheit, " degrees Celsius");
        var withDegrees = DegreeRx().Replace(withCelsius, " degrees");

        return PercentRx().Replace(withDegrees, " percent");
    }

    private static string CollapseWhitespace(string text) => WhitespaceRx().Replace(text, " ").Trim();

    // Innermost <think>...</think> pair only: the negative lookahead bars the content from
    // containing a further "<think>", so a nested block's outer opening tag is never consumed
    // until its inner block has already been resolved by an earlier pass (see StripThinkBlocks).
    [GeneratedRegex(@"<think>(?:(?!<think>).)*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRx();

    [GeneratedRegex(@"<think>.*$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UnclosedThinkBlockRx();

    // Any leftover </think> literal, orphaned or otherwise — belt-and-braces cleanup (F68.3).
    [GeneratedRegex(@"</think>", RegexOptions.IgnoreCase)]
    private static partial Regex OrphanThinkCloseRx();

    // [text](url) -> text
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex LinkRx();

    // `code` -> code
    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRx();

    // #, ##, ... at line start -> removed
    [GeneratedRegex(@"^#{1,6}[ \t]*", RegexOptions.Multiline)]
    private static partial Regex HeadingRx();

    // **bold** -> bold
    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRx();

    // *italic* -> italic. Accepted limitation: a numeric asterisk run like "2*3*4" also matches
    // this shape and collapses to "234" — booth copy has no legitimate use for "*" as a
    // multiplication operator, so this is left alone as a conscious tradeoff, not an oversight.
    [GeneratedRegex(@"\*(.+?)\*")]
    private static partial Regex ItalicAsteriskRx();

    // _italic_ -> italic; \b keeps snake_case untouched (see StripMarkdown)
    [GeneratedRegex(@"\b_(.+?)_\b")]
    private static partial Regex ItalicUnderscoreRx();

    // Digit-anchored: 76°F -> 76 degrees Fahrenheit
    [GeneratedRegex(@"(?<=\d)°F")]
    private static partial Regex DegreeFahrenheitRx();

    // Digit-anchored: 20°C -> 20 degrees Celsius
    [GeneratedRegex(@"(?<=\d)°C")]
    private static partial Regex DegreeCelsiusRx();

    // Digit-anchored: 45° -> 45 degrees (run after the F/C variants above)
    [GeneratedRegex(@"(?<=\d)°")]
    private static partial Regex DegreeRx();

    // Digit-anchored: 50% -> 50 percent
    [GeneratedRegex(@"(?<=\d)%")]
    private static partial Regex PercentRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRx();
}
