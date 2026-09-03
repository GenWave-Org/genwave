namespace GenWave.Host.Catalog;

/// <summary>
/// One brand brief inside a <see cref="CatalogAdPackManifest"/>'s <c>briefs[]</c> (SPEC F162.2) — the
/// EXACT shape <c>Core.Domain.AdBriefUpsertInput</c> takes, minus <c>enabled</c> (T405 review RULING:
/// <c>enabled</c> is never the manifest's own business — a batched pack install always births a
/// brand-new brief enabled and always PRESERVES an existing one's own enabled flag, a fixed contract
/// <c>Core.Abstractions.IAdBriefStore.UpsertAllAsync</c> itself decides, never a per-brief value this
/// record — or <see cref="Api.AdPackController"/> — could even express). <see cref="Brand"/> is the
/// pack-install upsert key's other half (alongside the entry's own slug),
/// <see cref="Premise"/>/<see cref="Tone"/>/<see cref="Structure"/> are the SAME optional prompt hints
/// an owner-authored brief carries (SPEC F160.2 — the writer samples from these when set). Data only,
/// by construction — this record has no field capable of carrying a script, audio, or code, the
/// trust-boundary posture SPEC F162.2 states in words.
/// </summary>
public sealed record CatalogAdPackBrief(string Brand, string? Premise, string? Tone, string? Structure);
