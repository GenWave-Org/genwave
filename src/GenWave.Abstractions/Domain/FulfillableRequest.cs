namespace GenWave.Core.Domain;

/// <summary>
/// The projection <c>IRequestStore.GetOldestLiveAsync</c> returns for the oldest live pending request
/// (SPEC F87.6, STORY-227, PLAN T90) — exactly what the fulfillment rung needs to resolve it, and
/// nothing else. Never the wish text — the same F87.7/F87.8 non-goal <see cref="UnparsedRequest"/>'s
/// own remarks establish for its sibling projection.
/// </summary>
/// <param name="Id">
/// The row's identity — the same id <see cref="Abstractions.IRequestStore.TryMarkFulfilledAsync"/>
/// stamps for the one-shot CAS.
/// </param>
/// <param name="MatchedMediaId">
/// The T89 catalog match (<see cref="Abstractions.IRequestStore.MarkMatchedAsync"/>), or
/// <see langword="null"/> for a vibe-only request. <see cref="Abstractions.IRequestStore.GetOldestLiveAsync"/>'s
/// own WHERE clause only ever admits a row where this is populated OR <see cref="Moods"/> is
/// non-empty — never neither.
/// </param>
/// <param name="Moods">
/// The parsed vibe predicate (SPEC F85.1, resolved via the F86.8 mood-filter machinery); empty for a
/// matched request.
/// </param>
public sealed record FulfillableRequest(long Id, long? MatchedMediaId, IReadOnlyList<string> Moods);
