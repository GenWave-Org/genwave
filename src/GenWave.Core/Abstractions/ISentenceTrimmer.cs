using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.35, gh-#293) — pure sentence trimmer: drops every sentence of a copy that contains a
/// repetition hit phrase, so the rest of the DJ's copy can still air. No I/O.
/// </summary>
public interface ISentenceTrimmer
{
    /// <summary>
    /// Splits <paramref name="copy"/> on <c>.</c> <c>!</c> <c>?</c> <c>…</c> and drops each sentence that contains
    /// any of <paramref name="hits"/>. Abbreviation, initial and decimal periods are not sentence ends
    /// (<see cref="SentenceBoundaries"/>); a hit that runs across a sentence end drops every sentence it touches.
    /// No hits (or no match) leaves the copy unchanged with a dropped count of 0.
    /// </summary>
    TrimResult Trim(string copy, IReadOnlyList<RepetitionHit> hits);
}
