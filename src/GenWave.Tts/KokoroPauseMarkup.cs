namespace GenWave.Tts;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Engine-aware sentence-pause markup for the Kokoro request path (gh-#116). The pinned
/// kokoro-fastapi v0.6.0 inserts ZERO silence at punctuation — a whole blurb renders as one
/// breathless chunk — but honors exact <c>[pause:Ns]</c>/<c>[pause:N.Ns]</c> markup
/// (case-insensitive, seconds) as true digital silence. This helper appends one
/// <c>" [pause:Ns]"</c> tag after each sentence-final run of <c>. ! ? …</c> so kokoro-kind
/// renders breathe between sentences.
///
/// Kokoro-only by construction: applied inside <see cref="KokoroTtsSynthesizer"/> (primary) and
/// <see cref="KokoroFallbackRenderer"/> (kokoro-kind chain hops) at request build — BELOW the
/// <see cref="NormalizingTtsSynthesizer"/> chokepoint, so the normalized text, the gh-#161
/// corrections fingerprints, and <see cref="TtsSegmentSource"/>'s final cache key (computed from
/// pre-synthesis copy text one seam ABOVE the engine split) are all byte-identical to before —
/// and NEVER on the Piper path: piper-tts 1.6.0 has no pause mechanism, so a tag reaching
/// <see cref="PiperTtsSynthesizer"/> would be SPOKEN ALOUD. <c>LlmCopyWriter.CleanCopy</c> strips
/// every <c>[...]</c> from LLM copy upstream, which is exactly why the tag can only be added
/// here, per engine, and can never arrive embedded in the copy itself.
///
/// Heuristic — simple over clever, matched to the copy shapes the normalization corpus
/// (Story184) actually contains:
/// <list type="bullet">
/// <item>A maximal run of <c>. ! ? …</c> followed by whitespace-then-more-text gets exactly ONE
/// tag after the run — <c>...</c> / <c>….</c> / <c>?!</c> never stack pauses.</item>
/// <item>Mid-word punctuation never matches (the run must be followed by whitespace): decimals
/// ("101.5") and stylized names ("P!nk", "Ke$ha") pass through untouched.</item>
/// <item>A lone <c>.</c> closing a dotted single-letter abbreviation ("9 a.m.", "e.g.", "U.S.")
/// is skipped — even where it genuinely ends a sentence: an occasionally missing pause beats a
/// mid-sentence one after every "a.m.". Multi-letter abbreviations ("Mr.") are deliberately NOT
/// special-cased — none appear in booth copy shapes, and guessing would be clever, not
/// simple.</item>
/// <item>The final sentence-ender of the text never gets a tag (the followed-by-more-text
/// lookahead): a trailing pause is not an audible sentence gap — nothing follows it — just dead
/// tail that would inflate every clip's measured cue-out/DurationMs (the cue analyzer measures
/// the rendered file), deaden the crossfade into the next item, and play as flat dead air on the
/// preview/safe-segment paths that have no cue trim at all.</item>
/// </list>
/// </summary>
public static partial class KokoroPauseMarkup
{
    /// <summary>
    /// Returns <paramref name="text"/> with one pause tag appended after each qualifying sentence
    /// boundary. <paramref name="pauseSeconds"/> &lt;= 0 disables insertion (the
    /// <see cref="TtsOptions.SentencePauseSeconds"/> "0 = off" contract) and returns the text
    /// unchanged.
    /// </summary>
    public static string InsertSentencePauses(string text, double pauseSeconds)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (pauseSeconds <= 0)
            return text;

        // Invariant formatting: the wire contract is exactly [pause:Ns]/[pause:N.Ns] — a
        // comma-decimal host locale must never put "[pause:0,6s]" on the wire.
        var tag = " [pause:" + pauseSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s]";
        return SentenceEndRunRx().Replace(text, match =>
            IsDottedAbbreviationDot(text, match) ? match.Value : match.Value + tag);
    }

    /// <summary>
    /// True when the match is a lone <c>.</c> that closes a dotted single-letter abbreviation —
    /// the two characters before it are a letter preceded by another dot ("a.m<b>.</b>",
    /// "U.S<b>.</b>"). Runs ("...", "?!") are never abbreviation dots.
    /// </summary>
    static bool IsDottedAbbreviationDot(string text, Match match) =>
        match.Value == "."
        && match.Index >= 2
        && char.IsLetter(text[match.Index - 1])
        && text[match.Index - 2] == '.';

    // A maximal sentence-ender run with more text after it: the lookahead requires whitespace and
    // then a non-whitespace character, so an end-of-text run (no trailing pause — see the class
    // remarks) and mid-word punctuation (P!nk, 101.5) never match at all.
    [GeneratedRegex(@"[.!?…]+(?=\s+\S)")]
    private static partial Regex SentenceEndRunRx();
}
