using System.Text;
using System.Text.RegularExpressions;

namespace GenWave.Tts;

/// <summary>
/// The sound tag gate (SPEC F219.17/.18): a sound tag is a bare <c>[word]</c> or <c>[two words]</c> (letters and spaces only).
/// <see cref="Apply"/> keeps the profile's allowed tags up to a cap, first ones winning, and removes the rest;
/// <see cref="Strip"/> removes every tag, giving the tag-free copy the truth gate, repetition check and utterance record read.
/// Other bracket markup is never touched: <c>[pause:0.6s]</c> (has a colon and digits) and <c>[word](/ipa/)</c> (a link
/// annotation follows) do not match. A kept tag is emitted in the profile's own spelling (<c>[Laugh]</c> becomes <c>[laugh]</c>), since the
/// engine only knows that form. Whitespace is tidied only where a tag was removed; the rest of the text is left byte-for-byte. Pure.
/// </summary>
public static partial class SoundTags
{
    // [tag] not followed by a (...) annotation (same adjacency SpeechText.MarkupSpanRx treats as one markup token).
    [GeneratedRegex(@"\[[A-Za-z][A-Za-z ]*\](?! ?\()")]
    private static partial Regex TagRx();

    /// <summary>
    /// Keeps the first <paramref name="cap"/> tags that are in the profile's list, written as the profile spells them; removes
    /// extra and unknown tags.
    /// </summary>
    public static string Apply(string text, VoiceEngineProfile profile, int cap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(profile);

        return ApplyAcross([text], profile.SoundTags, cap)[0];
    }

    /// <summary>
    /// <see cref="Apply"/> over several texts in order with ONE running count, so the first <paramref name="cap"/> listed tags win
    /// across all of them (a whole crosstalk exchange, SPEC F219.17). Every other tag is removed.
    /// </summary>
    public static IReadOnlyList<string> ApplyAcross(IReadOnlyList<string> texts, IReadOnlyList<string> tags, int cap)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(tags);

        if (cap <= 0 || tags.Count == 0)
            return [.. texts.Select(Strip)];

        var kept = 0;
        return [.. texts.Select(text => Rewrite(text, tag =>
        {
            if (kept >= cap)
                return null;
            var spelling = tags.FirstOrDefault(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
            if (spelling is not null)
                kept++;
            return spelling;
        }))];
    }

    /// <summary>True when <paramref name="bracket"/> (a whole <c>[tag]</c>) names one of <paramref name="tags"/>, ignoring case. A null or empty list lists nothing.</summary>
    public static bool IsListed(string bracket, IReadOnlyList<string>? tags)
    {
        ArgumentNullException.ThrowIfNull(bracket);
        return tags is { Count: > 0 } && tags.Any(t => string.Equals(t, bracket, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes every sound tag, tidying whitespace at each removal.</summary>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Rewrite(text, static _ => null);
    }

    /// <param name="replacement">The text to emit for a tag, or null to remove it.</param>
    private static string Rewrite(string text, Func<string, string?> replacement)
    {
        if (!TagRx().IsMatch(text))
            return text;

        var result = new StringBuilder(text.Length);
        var cursor = 0;

        foreach (Match match in TagRx().Matches(text))
        {
            result.Append(text, cursor, match.Index - cursor);
            cursor = match.Index + match.Length;

            var spelling = replacement(match.Value);
            if (spelling is not null)
            {
                result.Append(spelling);
                continue;
            }

            cursor = RemoveTag(text, result, cursor);
        }

        return result.Append(text, cursor, text.Length - cursor).ToString();
    }

    // Eats the spaces around a removed tag and puts back one only where the text needs it: never at the start or end of a line,
    // right after an opener ( [ { or an opening quote, or right before a closer ) ] } or a closing quote or .,!?;: mark. A tag
    // glued between two letters or digits ("hi[sigh]there") also gets one, so words never fuse. Returns the new cursor, past the
    // eaten trailing spaces.
    private static int RemoveTag(string text, StringBuilder result, int afterTag)
    {
        var leading = false;
        while (result.Length > 0 && IsHorizontalSpace(result[^1]))
        {
            result.Length--;
            leading = true;
        }

        var next = afterTag;
        while (next < text.Length && IsHorizontalSpace(text[next]))
            next++;
        var trailing = next > afterTag;

        if (NeedsSeparator(text, result, next, leading || trailing))
            result.Append(' ');

        return next;
    }

    private static bool NeedsSeparator(string text, StringBuilder result, int next, bool hadSpace)
    {
        var atLineStart = result.Length == 0 || result[^1] == '\n';
        var atLineEnd = next >= text.Length || text[next] is '\n' or '\r';
        if (atLineStart || atLineEnd)
            return false;

        var before = result[^1];
        var after = text[next];
        if (OpensBefore(result) || ClosesAt(text, next) || ".,!?;:".Contains(after))
            return false;

        return hadSpace || (char.IsLetterOrDigit(before) && char.IsLetterOrDigit(after));
    }

    // The last written char opens a span: ( [ {, or a quote that itself starts a word (the char before it is nothing, a space or an opener).
    private static bool OpensBefore(StringBuilder result)
    {
        var last = result[^1];
        if (last is '(' or '[' or '{')
            return true;
        if (!IsQuote(last))
            return false;
        return result.Length == 1 || IsHorizontalSpace(result[^2]) || result[^2] is '\n' or '(' or '[' or '{';
    }

    // The char at <next> closes a span: ) ] }, or a quote that ends a word (followed by nothing, a space, a mark or a closer).
    private static bool ClosesAt(string text, int next)
    {
        var c = text[next];
        if (c is ')' or ']' or '}')
            return true;
        if (!IsQuote(c))
            return false;
        var after = next + 1;
        return after >= text.Length || char.IsWhiteSpace(text[after]) || ".,!?;:)]}".Contains(text[after]);
    }

    private static bool IsQuote(char c) => c is '"' or '\'' or '\u201C' or '\u201D' or '\u2018' or '\u2019';

    private static bool IsHorizontalSpace(char c) => c is ' ' or '\t';
}
