namespace GenWave.Core.Domain;

/// <summary>
/// The fields <c>Abstractions.ISponsorStore.CreateOwnerAsync</c> needs to land a new owner-authored
/// <c>station.sponsor</c> row (SPEC F171.1; STORY-406; PLAN T432). <c>pack_slug</c> is never part of
/// this shape — the store hardcodes it <see langword="null"/> (the <c>AdBrief.CreateOwnerAsync</c>
/// precedent, one table over).
/// </summary>
public sealed record NewSponsor(
    string Name,
    string? Tagline,
    string? About,
    string? Phone,
    string? Address,
    string? Website,
    string? Tone);
