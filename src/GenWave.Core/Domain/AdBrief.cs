namespace GenWave.Core.Domain;

/// <summary>
/// One row of <c>station.ad_brief</c> (SPEC F159.1; STORY-389; PLAN T398) —
/// <c>Abstractions.IAdBriefStore</c>'s own element type. <see cref="PackSlug"/> is
/// <see langword="null"/> for an owner-authored brief, non-null for a pack-installed one (SPEC
/// F162.2's own upsert key); either way, <c>(PackSlug, Brand)</c> is unique — Postgres
/// <c>UNIQUE NULLS NOT DISTINCT</c> collapses every owner-authored brief to ONE per
/// <see cref="Brand"/> (ratified by Dean 2026-09-02, SPEC F159.1 rider): a brand is a brand, an owner
/// re-authoring an existing one's brief updates it in place, never forks a second row.
/// </summary>
/// <param name="Id">The row's own surrogate key.</param>
/// <param name="PackSlug">The installing pack's slug, or <see langword="null"/> for an
/// owner-authored brief.</param>
/// <param name="Brand">The brand this brief is about — the upsert key's other half.</param>
/// <param name="Premise">The brand's premise hint, or <see langword="null"/> (SPEC F160.2 — the
/// prompt samples from this when set).</param>
/// <param name="Tone">The brand's tone hint, or <see langword="null"/>.</param>
/// <param name="Structure">The brand's structure hint, or <see langword="null"/>.</param>
/// <param name="Enabled">Whether the writer may sample this brief (SPEC F160.2) — an operator can
/// disable a brief without deleting it.</param>
/// <param name="CreatedAt">When this brief was first created — untouched by a later upsert.</param>
public sealed record AdBrief(
    long Id,
    string? PackSlug,
    string Brand,
    string? Premise,
    string? Tone,
    string? Structure,
    bool Enabled,
    DateTime CreatedAt);
