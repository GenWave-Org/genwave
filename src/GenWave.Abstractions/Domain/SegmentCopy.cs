namespace GenWave.Core.Domain;

/// <summary>
/// The spoken text an <see cref="Abstractions.ISegmentCopyWriter"/> produced for a
/// <see cref="SegmentRequest"/>, plus its cache provenance (SPEC F34.6). Provenance is a fact only the
/// writer knows: an LLM miss that falls back to template copy carries the SAME stable text a direct
/// template render would, so it is NOT fresh and must land in the ordinary forever-cache — only copy
/// an LLM actually authored is fresh-per-airing and routes to the swept <c>blurbs/</c> subdirectory.
/// </summary>
/// <param name="Text">The text to synthesize.</param>
/// <param name="FreshPerAiring">
/// True when <paramref name="Text"/> is newly authored for this airing and must never be treated as a
/// stable, forever-cacheable string; false for template copy (including an LLM writer's fallback).
/// </param>
public sealed record SegmentCopy(string Text, bool FreshPerAiring)
{
    /// <summary>
    /// SPEC F218.1 (STORY-532, PLAN T684) — the <c>station.utterance</c> row the writer stored this copy
    /// as, or <see langword="null"/> when nothing was recorded (a templated kind, no persona, a store
    /// miss). Rides to the renderer so <see cref="Events.SegmentGenerated.UtteranceId"/> can name it.
    /// An init-only body property, never a positional parameter, so the published constructor and
    /// <c>Deconstruct</c> arity stay unchanged (the <see cref="SegmentRequest.CrosstalkAiredThisBreak"/> precedent).
    /// </summary>
    public long? UtteranceId { get; init; }
}
