namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for <c>GET /api/shows/{id}/rotation-pool</c> (SPEC F152.5, STORY-373, PLAN T362) — the
/// Shows page's own "live pool size" chip. <see cref="Eligible"/> is the count of playable tracks the
/// show's own rotation rule admits under the station default envelope right now, or
/// <see langword="null"/> ("unknown") when <see cref="Core.Abstractions.IMediaCatalog.GetEnvelopeCandidateCountAsync"/>
/// itself answers null (an empty rotation scope, or a test-double/pre-F152.5 catalog implementation
/// that has not opted into the real count). <see cref="Since"/> is the rotation ledger's own epoch
/// (<c>Gardener:RotationSince</c>, SPEC F149.3) — <see langword="null"/> only on a pre-Gardener
/// install whose one-shot seed migration has never run.
/// </summary>
public sealed record ShowRotationPoolDto(int? Eligible, DateTimeOffset? Since);
