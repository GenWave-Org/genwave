namespace GenWave.Core.Domain;

/// <summary>
/// One media id's rating state, as resolved by <see cref="Abstractions.IMediaRating.GetRatingsAsync"/>
/// (SPEC F33.2, F33.9). A media id with no row in <c>library.media_rating</c> resolves to the ledger
/// default (<see cref="Score"/> 50, <see cref="NeverPlay"/> false) — there is no backfill, so "never
/// rated" and "explicitly rated 50/playable" are indistinguishable by design.
/// </summary>
public sealed record MediaRating(string MediaId, int Score, bool NeverPlay);
