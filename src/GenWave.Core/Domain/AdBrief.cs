namespace GenWave.Core.Domain;

/// <summary>
/// One row of <c>station.ad_brief</c> (SPEC F159.1, F171.6; STORY-389, STORY-406; PLAN T398, T432) —
/// <c>Abstractions.IAdBriefStore</c>'s own element type. <see cref="PackSlug"/> is
/// <see langword="null"/> for an owner-authored brief, non-null for a pack-installed one (SPEC
/// F162.2's own upsert key); either way, <c>(SponsorId, Premise)</c> folded is unique
/// (<c>ad_brief_sponsor_id_premise_key</c>, db/46) — several angles are legal per sponsor (SPEC
/// F171.6: two different premises for the same sponsor are two distinct rows), but the SAME premise
/// re-authored for the SAME sponsor updates the existing row in place rather than forking a second one.
/// </summary>
/// <param name="Id">The row's own surrogate key.</param>
/// <param name="PackSlug">The installing pack's slug, or <see langword="null"/> for an
/// owner-authored brief.</param>
/// <param name="SponsorId">The sponsor this brief is about — the upsert key's other half
/// (<c>station.sponsor</c>, PLAN T432).</param>
/// <param name="Premise">The angle this brief takes, or <see langword="null"/> (SPEC F160.2 — the
/// prompt samples from this when set).</param>
/// <param name="Tone">The brief's tone hint, or <see langword="null"/>.</param>
/// <param name="Structure">The brief's structure hint, or <see langword="null"/>.</param>
/// <param name="Enabled">Whether the writer may sample this brief (SPEC F160.2) — an operator can
/// disable a brief without deleting it.</param>
/// <param name="CreatedAt">When this brief was first created — untouched by a later upsert.</param>
public sealed record AdBrief(
    long Id,
    string? PackSlug,
    long SponsorId,
    string? Premise,
    string? Tone,
    string? Structure,
    bool Enabled,
    DateTime CreatedAt);
