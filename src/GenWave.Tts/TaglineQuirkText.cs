namespace GenWave.Tts;

/// <summary>
/// SPEC F218.32 (Dean, 11-11) - swaps a spent tagline's quoted span in the quirk it was quoted from for
/// <see cref="Placeholder"/>, so the behaviour stays ("Cites years the way other people cite scores: '…'")
/// and the words do not, and the sentence keeps its grammar.
/// </summary>
static class TaglineQuirkText
{
    public const string Placeholder = "'…'";

    static readonly char[] QuoteMarks = ['"', '\'', '“', '”', '‘', '’'];

    /// <summary>
    /// Replaces the span and its quote marks with <see cref="Placeholder"/>. A quote mark counts only when it
    /// directly touches the span (no whitespace between), so spaced quotes such as <c>' go on '</c> are not
    /// handled. A quoted occurrence (a mark directly either side) wins over an earlier bare one; failing that
    /// the first bare match goes, taking at most one directly adjacent mark per side. A span not present
    /// returns the quirk as is.
    /// </summary>
    public static string Replace(string quirk, string span)
    {
        if (span.Length == 0)
            return quirk;

        var first = -1;
        for (var at = quirk.IndexOf(span, StringComparison.Ordinal); at >= 0;
             at = quirk.IndexOf(span, at + 1, StringComparison.Ordinal))
        {
            first = first < 0 ? at : first;
            var open = QuoteBefore(quirk, at);
            var close = QuoteAfter(quirk, at + span.Length);
            if (open >= 0 && close >= 0)
                return Splice(quirk, open, close);
        }

        if (first < 0)
            return quirk;

        var start = QuoteBefore(quirk, first);
        var end = QuoteAfter(quirk, first + span.Length);
        return Splice(quirk, start >= 0 ? start : first, end >= 0 ? end : first + span.Length);
    }

    // Index of the quote mark directly before the span; -1 if the preceding char is not one.
    static int QuoteBefore(string quirk, int at) => at > 0 && IsQuote(quirk[at - 1]) ? at - 1 : -1;

    // Index just past the quote mark directly after the span; -1 if the following char is not one.
    static int QuoteAfter(string quirk, int spanEnd) =>
        spanEnd < quirk.Length && IsQuote(quirk[spanEnd]) ? spanEnd + 1 : -1;

    static bool IsQuote(char c) => Array.IndexOf(QuoteMarks, c) >= 0;

    static string Splice(string quirk, int start, int end) => string.Concat(quirk.AsSpan(0, start), Placeholder, quirk.AsSpan(end));
}
