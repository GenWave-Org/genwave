namespace GenWave.Core.Domain;

/// <summary>
/// One brief's own payload inside a batched <see cref="Abstractions.IAdBriefStore.UpsertAllAsync"/>
/// call (SPEC F162.2, PLAN T405) — mirrors <see cref="AvatarPackItemInput"/>'s own per-item batch-input
/// shape one seam over. Carries no <c>enabled</c> field, deliberately: a batched upsert is ALWAYS a
/// pack install (<see cref="Abstractions.IAdBriefStore.UpsertAllAsync"/>'s own contract), where a
/// brand-new brief is always born enabled and an existing one's own enabled flag is always preserved
/// — <c>enabled</c> is the operator's own lever (SPEC F162.1), never this input's business.
/// </summary>
public sealed record AdBriefUpsertInput(string Brand, string? Premise, string? Tone, string? Structure);
