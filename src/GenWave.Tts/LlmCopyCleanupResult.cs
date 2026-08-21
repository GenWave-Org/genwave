namespace GenWave.Tts;

/// <summary>
/// Outcome of <see cref="LlmCopyWriter.CleanCopy"/>'s hygiene pass plus the F123.2 sentence-boundary
/// salvage (SPEC F123.2-F123.4, STORY-319, PLAN T263) — mirrors
/// <see cref="GenWave.Core.Domain.PersonaPreviewResult"/>'s closed-hierarchy shape so the three
/// outcomes (an exact fit, a salvaged trim, or a full reject) can never be confused with one another
/// the way a single nullable-string-plus-bool pair could — a trim always carries real text, a reject
/// never does, by construction rather than by convention. Internal: <see cref="LlmCopyWriter"/> is
/// this type's only producer and only consumer.
/// </summary>
internal abstract record LlmCopyCleanupResult
{
    LlmCopyCleanupResult() { }

    /// <summary>Cleaned text that already fit under <c>Llm:MaxCopyChars</c> as-is — no cut was needed.</summary>
    public sealed record Fits(string Text) : LlmCopyCleanupResult;

    /// <summary>
    /// The model's full reply exceeded <c>Llm:MaxCopyChars</c> after hygiene, but cutting at the
    /// LAST complete sentence that fits produced usable copy (SPEC F123.2). <see cref="Text"/> ends
    /// at a sentence boundary, never mid-sentence, by construction — see
    /// <see cref="LlmCopyWriter.CleanCopy"/>'s own remarks. <see cref="CharsBeforeTrim"/> is the
    /// fully-cleaned length BEFORE the cut, paired with <see cref="Text"/>'s own length for the
    /// F123.4 before-after observability line.
    /// </summary>
    public sealed record Trimmed(string Text, int CharsBeforeTrim) : LlmCopyCleanupResult;

    /// <summary>
    /// Nothing survives: hygiene left an empty string, or the reply is over-length and even its
    /// FIRST sentence already exceeds the cap (nothing complete to cut at) — the pre-F123 reject
    /// stands, byte-identical to before T263.
    /// </summary>
    /// <param name="WasOverLength">
    /// SPEC F139.1 (STORY-353, PLAN T330): <see langword="true"/> for the over-length-with-no-salvage
    /// case (a candidate existed but none survived <c>maxChars</c> — the gh-#277 family,
    /// <see cref="LlmCallCause.OverLength"/>); <see langword="false"/> when hygiene left an empty
    /// string outright (<see cref="LlmCallCause.EmptyCompletion"/>). Decided once, here at the source
    /// (<see cref="LlmCopyWriter.CleanCopy"/>), never re-derived downstream from anything about the
    /// text itself.
    /// </param>
    public sealed record Rejected(bool WasOverLength) : LlmCopyCleanupResult;
}
