namespace GenWave.MediaLibrary.ExplicitClassification;

/// <summary>
/// The constrained-output parse (SPEC F95.3, STORY-251, T113): pure, no I/O — turns whatever raw
/// text an LLM completion returned into a tri-state verdict.
/// <para>
/// Deliberately an EXACT-MATCH parse, NOT a "scan for the first recognizable word" one: this is a
/// safety control (a wrong verdict here is stamped permanently, fail-open), and a substring/token
/// scan is exploitable by the model naming the answer inside a longer sentence for an unrelated
/// reason — e.g. a reply like <c>"No Diggity: yes"</c> (an actual track title containing the word
/// "No") would scan-match "no" FIRST and invert the verdict. Trimmed + case-folded + stripped of
/// surrounding quotes/punctuation, the WHOLE remaining reply must equal exactly "yes", "no", or
/// "unknown" — a hedge ("unknown, hard to tell"), a verbose non-answer ("There is no way to tell
/// from the title alone"), or any other wrong-shaped output all collapse to the SAME <see
/// langword="null"/> miss as an explicit "unknown" answer — never a partial verdict, and never a
/// guess extracted from the middle of a longer reply.
/// </para>
/// </summary>
static class ExplicitClassificationParser
{
    /// <summary>
    /// Trimmed from both ends of the reply before the exact-match compare: ordinary whitespace plus
    /// the quoting/sentence punctuation a constrained-output model tends to wrap a single-word answer
    /// in (e.g. <c>"Yes."</c>, <c>'no'</c>, <c>Unknown!</c>). <see cref="string.Trim(char[])"/> strips
    /// every leading/trailing character in this set, not just one occurrence, so nested cases like
    /// <c>"  \"yes\".  "</c> reduce to bare <c>yes</c> in one pass.
    /// </summary>
    static readonly char[] SurroundingChars = [' ', '\t', '\r', '\n', '"', '\'', '.', ',', '!', '?', ';', ':'];

    public static bool? Parse(string raw) =>
        raw.ToLowerInvariant().Trim(SurroundingChars) switch
        {
            "yes" => true,
            "no" => false,
            "unknown" => null,

            // Wrong-shaped output (extra words, a hedge, no recognizable answer at all) is the SAME
            // miss outcome as an explicit "unknown" answer — never a partial verdict, and never a
            // guess plucked from somewhere inside the reply.
            _ => null,
        };
}
