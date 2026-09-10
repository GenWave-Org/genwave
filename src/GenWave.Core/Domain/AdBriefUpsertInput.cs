namespace GenWave.Core.Domain;

/// <summary>
/// One brief's own payload inside a batched <see cref="Abstractions.IAdBriefStore.UpsertAllAsync"/>
/// call (SPEC F162.2, F171.6; PLAN T405, T432) — mirrors <see cref="AvatarPackItemInput"/>'s own
/// per-item batch-input shape one seam over. Carries no <c>enabled</c> field, deliberately: a batched
/// upsert is ALWAYS a pack install (<see cref="Abstractions.IAdBriefStore.UpsertAllAsync"/>'s own
/// contract), where a brand-new brief is always born enabled and an existing one's own enabled flag
/// is always preserved — <c>enabled</c> is the operator's own lever (SPEC F162.1), never this input's
/// business. <see cref="SponsorId"/> is resolved by the caller BEFORE building this input — a pack
/// install first turns each manifest brand into a sponsor id
/// (<see cref="Abstractions.ISponsorStore.UpsertPackSponsorsAsync"/>), then batches the briefs.
/// </summary>
public sealed record AdBriefUpsertInput(long SponsorId, string? Premise, string? Tone, string? Structure);
