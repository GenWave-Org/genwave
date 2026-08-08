namespace GenWave.Core.Domain;

/// <summary>
/// What an <see cref="Abstractions.IContextProvider"/> hands back on a successful fetch (SPEC F107.1):
/// plain-text facts for the copywriter to render into on-air copy, plus the caching horizon the
/// pipeline must respect. Never prose-for-air on its own — <see cref="SegmentFacts"/> is the raw
/// material a segment's copy is written FROM, not the copy itself.
/// </summary>
/// <param name="SegmentFacts">
/// Plain-text facts for the segment-lane copywriter prompt (F107.3) — never spoken verbatim; the LLM
/// paraphrases/reads them under a "read these facts, do not add facts" posture.
/// </param>
/// <param name="PatterFact">
/// One compact fact for the patter-lane prompt (F107.5) — at most one context line per break, and
/// only when it fits. Null when the provider has nothing compact enough for patter, or nothing at
/// all.
/// </param>
/// <param name="FreshUntil">
/// The pipeline caching horizon: this content may be reused for any segment/patter airing up to (but
/// not including) this instant, after which the provider must be fetched again before it airs.
/// </param>
public sealed record ContextContent(string SegmentFacts, string? PatterFact, DateTimeOffset FreshUntil);
