using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GenWave.Tts;

/// <summary>
/// The single normalization chokepoint every booth-bound string passes through immediately before
/// TTS (SPEC F68). Pure and static: no I/O, no settings reads, zero non-BCL dependencies —
/// corrections are passed in by the caller. Passes run in a fixed order: think-strip, then
/// markdown strip, then HTML-entity decode, then think-strip again, then operator corrections, then
/// digit-anchored unit expansion, then entity-safe <c>&amp;</c>-to-"and", then the speakability
/// flatten, then whitespace collapse (F68.2). Think-strip runs first because nothing downstream
/// should ever process leaked reasoning text; markdown runs before entity decode so a bolded degree
/// symbol still reaches the unit-expansion pass; think-strip runs a second time immediately after
/// entity decode because an HTML-encoded <c>&amp;lt;think&amp;gt;</c> block only becomes a literal
/// tag at that point — the first pass never saw it (F68.3); operator corrections run after cleanup
/// (so a rule matches the readable text an operator sees in admin) and before the built-in
/// expansions (so a correction can pre-empt one); the flatten runs after every pass that authors or
/// rewrites words (gh-#541 — see <see cref="FlattenForSpeech"/> for the ruling and its three
/// deliberate survivors) so nothing can re-introduce the punctuation and casing it removes;
/// whitespace collapse runs last to tidy whatever the earlier passes left behind.
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
        var flattened = FlattenForSpeech(withAnd);

        return CollapseWhitespace(flattened);
    }

    /// <summary>
    /// Runs every normalization pass that precedes operator corrections — think-strip,
    /// markdown-strip, HTML-entity decode, then think-strip again — the exact text
    /// <see cref="SpeechCorrectionSet.Apply"/> matches against inside <see cref="Normalize"/>
    /// (F68.2). Internal (same-assembly) rather than a second public overload of
    /// <see cref="Normalize"/>, so <see cref="NormalizingTtsSynthesizer"/> can determine which rules
    /// would actually fire for observability (SPEC F68.7) without re-deriving this pipeline or
    /// guessing from raw, pre-cleanup text — and without disturbing <see cref="Normalize"/>'s own
    /// signature/overload set (STORY-185's single-call-site guard).
    /// </summary>
    internal static string PrepareForCorrections(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var scrubbed = StripThinkBlocks(text);
        var withoutMarkdown = StripMarkdown(scrubbed);
        var decoded = WebUtility.HtmlDecode(withoutMarkdown);

        // An HTML-encoded think tag (e.g. "&lt;think&gt;secret&lt;/think&gt;") is not literal
        // "<think>...</think>" until AFTER HtmlDecode runs above — the first StripThinkBlocks pass
        // never saw it as a tag at all, just inert text. Stripping again here closes that gap
        // (F68.3): a fully-encoded block, and a block that mixes literal and encoded tags at
        // different nesting levels, both resolve to plain literal tags by this point and are
        // removed the same way. Idempotent on text with no encoded tags, so this is a no-op pass
        // for the overwhelming common case.
        return StripThinkBlocks(decoded);
    }

    private static string StripThinkBlocks(string text)
    {
        // ThinkBlockRx only ever matches an innermost pair (its content is guaranteed free of a
        // further nested "<think>"), so a single Replace resolves one level of nesting at a time.
        // TextFixpoint peels nested blocks from the inside out until none remain — this is what
        // keeps a doubly-nested block from leaking its outer layer's text (F68.3).
        //
        // Capped at MaxThinkNestingDepth passes (T28 review carry-forward, wired live in T29): far
        // deeper nesting than any real LLM output produces, so this only ever bites a pathological
        // input. On cap-hit the loop falls through to the unclosed/orphan strips below rather than
        // spinning — the same "never stall the render path" discipline as SpeechCorrectionSet's own
        // 250ms per-rule match timeout.
        var stripped = TextFixpoint.Apply(text, MaxThinkNestingDepth, current => ThinkBlockRx().Replace(current, string.Empty));

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

    /// <summary>
    /// The speakability flatten (gh-#541, subsuming gh-#292's comma-vocative and gh-#432's
    /// mid-sentence-capitals pauses): both pinned engines read punctuation and casing as prosody
    /// cues and stumble on exactly the marks grammatically correct copy is full of, so booth copy
    /// is flattened to what the voice can actually speak — lowercase words, digits, and sentence
    /// enders. Dean's gh-#541 ruling ("ToLower and discard anything that isn't a-z") is applied
    /// with three deliberate survivors, each because removal would be WORSE on air than a pause:
    /// <list type="bullet">
    /// <item>Sentence enders (<c>.</c> <c>!</c> <c>?</c>) — <see cref="KokoroPauseMarkup"/>'s
    /// sentence-pause splice (gh-#116) and the blurb cue analyzer both key off them; dropping them
    /// collapses all prosody into one breathless run. Runs collapse to their first mark and
    /// ellipses become plain spaces — an ellipsis is a pause instruction, the exact thing this
    /// pass exists to remove.</item>
    /// <item>Digits — "76 degrees" with the 76 discarded is mangled copy, and the unit expansion
    /// one pass earlier is digit-anchored. HOW an engine reads a number aloud is gh-#211's lexicon
    /// problem, not a character-class one.</item>
    /// <item>Intra-word marks — a mark with a letter or digit on BOTH sides is identity, not
    /// prosody: "we'll" stripped to "well" airs a different word, and the F68 survival law pins
    /// stylized names ("Ke$ha", "AC/DC", "P!nk", snake_case) through this chokepoint — a raw a-z
    /// filter would rename them on air. Loose marks (quoting: <c>'iceberg'</c>; elision:
    /// <c>comin'</c>; the spaced pause-dash — all gh-#541 exhibits) are exactly the prosody cues
    /// the ruling removes. The one amendment this pass makes to the survival law is case: names
    /// flatten to lowercase like every other word, because casing is precisely gh-#432's pause
    /// trigger and neither engine spells a name back out loud.</item>
    /// </list>
    /// Accents fold to their base letters first (é → e) so a name is never silently truncated the
    /// way a raw a-z filter would truncate it. <c>[...]</c>-shaped speech-markup tokens (with an
    /// optional simple <c>(...)</c> annotation — the <see cref="PiperSpeechMarkup"/> vocabulary)
    /// pass through VERBATIM: authored segments and operator corrections may legally carry
    /// <c>[pause:Ns]</c> or <c>[word](/ipa/)</c> this far down, and flattening a directive turns
    /// it into spoken garbage. A nested-paren annotation (<c>[x](/mə(k)laʊd/)</c>) is preserved
    /// only through its first balanced paren — accepted limitation, matching real producers, which
    /// never nest (see <see cref="KokoroSpeechMarkup"/>).
    /// </summary>
    internal static string FlattenForSpeech(string text)
    {
        var result = new StringBuilder(text.Length);
        var cursor = 0;

        foreach (Match span in MarkupSpanRx().Matches(text))
        {
            result.Append(FlattenSegment(text[cursor..span.Index]));
            result.Append(span.Value);
            cursor = span.Index + span.Length;
        }

        result.Append(FlattenSegment(text[cursor..]));
        return result.ToString();
    }

    private static string FlattenSegment(string text)
    {
        // Accent fold before anything case-sensitive: decompose, drop the combining marks, and
        // recompose what remains — "Beyoncé" reaches the residual filter as "beyonce", a name,
        // not "beyonc", a truncation.
        var folded = CombiningMarkRx().Replace(text.Normalize(NormalizationForm.FormD), string.Empty)
            .Normalize(NormalizationForm.FormC);

        // Typographic variants map onto the ASCII mark that carries their keep-rule below —
        // a curly apostrophe in "we’ll" must survive exactly like the straight one.
        var mapped = folded
            .Replace('‘', '\'').Replace('’', '\'')
            .Replace('–', '-').Replace('—', '-');

        var lowered = mapped.ToLowerInvariant();
        var noEllipses = EllipsisRx().Replace(lowered, " ");
        var singleEnders = EnderRunRx().Replace(noEllipses, "$1");
        var noClauseMarks = ClauseMarkRx().Replace(singleEnders, " ");
        var filtered = LooseMarkRx().Replace(noClauseMarks, " ");

        // Re-attach an ender orphaned by a removal to its word ("iceberg ." → "iceberg.") so the
        // engines never receive a floating mark to stumble on.
        return OrphanedEnderRx().Replace(filtered, "$1");
    }

    /// <summary>
    /// Collapses every run of whitespace to one space and trims the ends — the exact rule every
    /// caller in this assembly that needs whitespace tidied after a strip pass must use, so it
    /// never has to be re-asserted or re-implemented elsewhere (<see cref="PiperSpeechMarkup.Strip"/>
    /// is the other caller). Internal (same-assembly) rather than a public utility: this is a
    /// normalization-pipeline detail, not a general-purpose string helper this assembly wants to
    /// advertise outward.
    /// </summary>
    internal static string CollapseWhitespace(string text) => WhitespaceRx().Replace(text, " ").Trim();

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

    // A [...]-shaped speech-markup token plus its optional simple (...) annotation — the exact
    // adjacency PiperSpeechMarkup recognises (at most one non-newline space between ] and the
    // opening paren). Deliberately non-nesting on both sides: real producers never nest (see
    // FlattenForSpeech remarks), and a conservative match here only means a pathological token
    // gets flattened instead of preserved — never that prose is wrongly skipped.
    [GeneratedRegex(@"\[[^\[\]]*\](?: ?\([^()]*\))?")]
    private static partial Regex MarkupSpanRx();

    // Combining marks left by FormD decomposition — removing them IS the accent fold (é -> e).
    [GeneratedRegex(@"\p{Mn}")]
    private static partial Regex CombiningMarkRx();

    // An ellipsis in either spelling ("..." or the single … glyph) is a pause instruction, not a
    // sentence ender — it becomes a plain space (see FlattenForSpeech).
    [GeneratedRegex(@"\.{2,}|…+")]
    private static partial Regex EllipsisRx();

    // "what?!" / "no!!!" -> the first mark speaks for the run.
    [GeneratedRegex(@"([.!?])[.!?]+")]
    private static partial Regex EnderRunRx();

    // Clause punctuation — the gh-#292/#303 stumble marks. Commas fall here by Dean's gh-#541
    // ruling: the prompt-side ban (Issue303_CommaDiscipline) asks the model nicely; this enforces.
    [GeneratedRegex(@"[,;:]")]
    private static partial Regex ClauseMarkRx();

    // The intra-word-survivor rule in one expression: any mark outside the speakable alphabet
    // (words, digits, whitespace, sentence enders) that is missing a letter or digit on either
    // side is loose — prosody, not identity — and becomes a space. What this leaves behind is by
    // construction intra-word ("we'll", "ke$ha", "ac/dc", "brass-and-glass", snake_case) and is
    // kept verbatim: renaming a stylized artist on air is worse than any pause (the F68 survival
    // law, amended only for case — see FlattenForSpeech). Lookarounds read the ORIGINAL text, so
    // one loose mark in a run condemns its neighbours the way "-'" after a word falls together.
    [GeneratedRegex(@"(?<![a-z0-9])[^a-z0-9\s.!?]+|[^a-z0-9\s.!?]+(?![a-z0-9])")]
    private static partial Regex LooseMarkRx();

    // "iceberg ." -> "iceberg." — an ender orphaned by a removal re-attaches to its word.
    [GeneratedRegex(@"\s+([.!?])")]
    private static partial Regex OrphanedEnderRx();
}
