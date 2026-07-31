namespace GenWave.Tts;

using System.Text.RegularExpressions;

/// <summary>
/// Defense-in-depth strip guard for the Piper request path (SPEC F96.3): piper-tts 1.6.0 has no
/// speech-markup mechanism at all and SPEAKS ALOUD any <c>[...]</c>-shaped token it receives.
/// Nothing on the Piper path is expected to still be carrying one this far down —
/// <c>LlmCopyWriter.CleanCopy</c> strips every <c>[...]</c> from LLM copy upstream, and Piper
/// requests never run through <see cref="KokoroSpeechMarkup"/> or <see cref="KokoroPauseMarkup"/>
/// — but an operator's correction replacement or an authored segment can contain brackets that no
/// LLM-copy filter ever saw. This removes them anyway, per F96.4: an unsupported markup form is
/// never a render failure, it is removed, and the words still air.
///
/// <para>Two token shapes, two outcomes:</para>
/// <list type="bullet">
/// <item><c>[pause:0.6s]</c> — a bare bracket token with nothing to speak — is removed ENTIRELY,
/// brackets and content both. A <c>[pause:Ns]</c>-shaped word is NEVER promoted to a kept word by
/// whatever happens to follow it — not even a well-formed parenthetical that merely sits next to it
/// in the source text (<c>[pause:0.6s] (a classic)</c> is not "the word `pause:0.6s`, annotated");
/// this is the one bracket content this type recognises by shape rather than by what follows it,
/// because it is the one directive vocabulary this codebase's markup producers actually emit (see
/// <see cref="KokoroPauseMarkup"/>).</item>
/// <item><c>[MacLeod](/məˈklaʊd/)</c> — a bracket token followed (immediately, or across a single
/// non-newline space — see below) by a parenthesized annotation — has the brackets and the
/// annotation stripped but keeps the word inside the brackets: the DJ still says "MacLeod", just
/// without the phoneme hint an engine that cannot honour it would otherwise speak verbatim. A word
/// whose annotation was ATTEMPTED but never balances (an unclosed <c>[MacLeod](/x/</c> with no
/// matching <c>)</c> anywhere) still keeps its word for the identical reason a nested annotation
/// does (below): the source proves it was authored as an annotated word, not a bare directive, and
/// a malformed annotation is no more grounds to drop it than a well-formed one.</item>
/// </list>
///
/// <para>
/// The annotation's parentheses may nest to any depth (<c>[MacLeod](New Order (1983))</c>,
/// <c>[MacLeod](/mə(k)laʊd/)</c> — parenthesized IPA is legitimate notation) — the annotation
/// group matches balanced parens, not just a single non-nested run, so a nested closing paren can
/// never truncate the annotation early and strand a bare-looking <c>[word]</c> that would
/// otherwise be wrongly treated as a directive and have its word deleted (F96.4 regression this
/// guards against: a word must never be dropped just because its annotation happens to nest, or
/// happens to be truncated). The annotation must otherwise be ADJACENT to the closing bracket — the
/// gap between <c>]</c> and <c>(</c> tolerates at most one non-newline space
/// (<c>[MacLeod] (/x/)</c> is still one token) but never more than that and never a newline: an
/// unrelated parenthetical several characters or a line away must never be mistaken for this
/// token's annotation and swallow content that was never part of it.
/// </para>
///
/// <para>
/// A single replace pass only ever resolves the innermost bracket pair, so a malformed or
/// adversarially nested <c>[...]</c>-shaped token (<c>[a[b]c]</c>, <c>[[MacLeod]](/x/)</c>) could
/// otherwise leave a bracket- or paren-shaped remnant behind for a wire that only ever sees the
/// result of ONE pass. <see cref="Strip"/> therefore loops to a fixpoint via
/// <see cref="TextFixpoint"/> — the exact shared helper <see cref="SpeechText"/>'s think-block
/// stripper uses for the identical "a nestable token must not leak" reason, so the cap and the
/// fixpoint condition itself cannot drift between the two — capped at
/// <see cref="MaxNestingDepth"/> passes so a pathological input can never spin the render path
/// (real markup never nests anywhere near that deep). <see cref="MarkupTokenRx"/> also carries its
/// own bounded match timeout (below): an adversarial unclosed annotation can force pathological
/// backtracking within a single pass, well short of the nesting cap, so <see cref="Strip"/> treats
/// a timed-out pass as a no-op rather than letting the exception propagate — see its own remarks.
/// </para>
///
/// <para>
/// Token removal can leave doubled whitespace behind where a bare directive sat mid-sentence
/// (<c>"Hello [emphasis:strong] World."</c> → <c>"Hello  World."</c> before collapsing) — this
/// runs strictly AFTER <see cref="SpeechText"/>'s own whitespace-collapse pass (SPEC F96.3, Piper
/// requests are built from already-normalized text), so nothing downstream would otherwise ever
/// re-run that collapse. <see cref="Strip"/> therefore calls
/// <see cref="SpeechText.CollapseWhitespace"/> itself as its final step — the identical rule, not a
/// second implementation of it.
/// </para>
/// </summary>
public static partial class PiperSpeechMarkup
{
    // Fixpoint cap, mirrors SpeechText.MaxThinkNestingDepth: far deeper bracket nesting than any
    // real markup produces, so this only ever bites a pathological input, never a real one.
    private const int MaxNestingDepth = 64;

    /// <summary>Removes every <c>[...]</c>-shaped token from <paramref name="text"/> per the class remarks.</summary>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var stripped = TextFixpoint.Apply(text, MaxNestingDepth, StripOnePass);
        return SpeechText.CollapseWhitespace(stripped);
    }

    // One MarkupTokenRx pass over `source`. On RegexMatchTimeoutException — adversarial unclosed-
    // annotation input forcing pathological backtracking within a single match attempt (T133
    // round-4 review), well short of MaxNestingDepth passes — returns `source` UNCHANGED rather
    // than letting the exception propagate: TextFixpoint's own unchanged-check then ends the loop
    // exactly as a genuine fixpoint would, so Strip falls through with whatever the last
    // successful pass already resolved (F96.4 — markup removal is never a render failure) instead
    // of faulting the whole render/feeder path (gh-#184's exact failure mode). Bounded by the same
    // 250ms match timeout LiteralRegexPosture applies to every operator/persona-authored pattern
    // elsewhere in this assembly (see MarkupTokenRx below).
    private static string StripOnePass(string source)
    {
        try
        {
            return MarkupTokenRx().Replace(source, match => ResolveToken(source, match));
        }
        catch (RegexMatchTimeoutException)
        {
            return source;
        }
    }

    // Decides what one MarkupTokenRx match resolves to, per the class remarks' two-shapes model:
    //   - a [pause:Ns]-shaped word is ALWAYS dropped whole, regardless of what follows it;
    //   - a word whose annotation fully matched (balanced) is kept, annotation dropped;
    //   - a word whose annotation was merely ATTEMPTED (a paren starts right after "]", one
    //     non-newline space tolerated) but never balances is still kept — the attempt proves it
    //     was authored as a word, not a bare directive;
    //   - anything else (nothing paren-shaped follows at all) is a bare directive: dropped whole.
    private static string ResolveToken(string source, Match match)
    {
        var word = match.Groups["word"].Value;
        if (PauseDirectiveRx().IsMatch(word))
            return string.Empty;

        return match.Groups["annotation"].Success || AnnotationAttemptFollows(source, match.Index + match.Length)
            ? word
            : string.Empty;
    }

    // True when the character(s) right after a bracket token's "]" look like the start of an
    // annotation — an opening paren, optionally across the same single non-newline space
    // MarkupTokenRx's own annotation group tolerates — regardless of whether that annotation goes
    // on to actually balance.
    private static bool AnnotationAttemptFollows(string source, int index)
    {
        if (index < source.Length && source[index] is ' ' or '\t')
            index++;

        return index < source.Length && source[index] == '(';
    }

    // A bracketed token, optionally followed — immediately or across at most one non-newline space
    // — by a parenthesized annotation: [word] or [word](annotation) / [word] (annotation). Whether
    // the annotation group matched (a BALANCED parenthesized run — the (?<Depth>...)/
    // (?<-Depth>...) pair below, so nested parens inside it, e.g. parenthesized IPA or a title
    // carrying its own "(year)", can never truncate the match early) is what ResolveToken uses,
    // together with an unbalanced-annotation-attempt check it makes separately against the source,
    // to decide whether the bracketed content is a spoken word or a directive with nothing worth
    // speaking (F96.4). The gap before the annotation's opening paren is capped at one non-newline
    // space precisely so an unrelated parenthetical several characters or a line away is never
    // captured as if it belonged to this token.
    // The only balancing-group regex in this assembly — its backtracking is boundable but not
    // linear on adversarial unclosed-annotation input, so it carries the same 250ms match timeout
    // LiteralRegexPosture applies to every operator/persona-authored pattern (StripOnePass's
    // try/catch above is what actually acts on a timeout here).
    [GeneratedRegex(
        @"\[(?<word>[^\[\]]*)\](?<annotation>[ \t]?\((?:[^()]|(?<Depth>\()|(?<-Depth>\)))*(?(Depth)(?!))\))?",
        RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex MarkupTokenRx();

    // A [pause:Ns]/[pause:N.Ns] bracket's own word content (case-insensitive, digits, optional
    // decimal, seconds suffix) — the exact shape KokoroPauseMarkup.FormatTag's tag carries minus
    // the surrounding "[ " / "]". Matched against ONLY the "word" group's text, never the
    // surrounding source, so it recognises the directive regardless of what happens to follow it.
    [GeneratedRegex(@"^pause:\d+(?:\.\d+)?s$", RegexOptions.IgnoreCase)]
    private static partial Regex PauseDirectiveRx();
}
