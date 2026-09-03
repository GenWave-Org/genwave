using GenWave.Ads;

namespace GenWave.Host.Api;

/// <summary>
/// The wire shape <see cref="AdsController"/> projects every <c>GenWave.Core.Domain.AdSpot</c> row
/// into (SPEC F162.1; STORY-392; PLAN T403) — "Host owns its own wire DTOs" (see
/// <c>AnnouncementHistoryDto</c>'s own remarks for the house convention), so a future Core-side field
/// never leaks onto the wire without a deliberate projection change here. <see cref="Source"/>/
/// <see cref="State"/> ride as their own snake_case wire tokens (<c>AdSourceTokens</c>/
/// <c>AdStateTokens</c>) — the same "enum as a stable string, never the C# name" posture every other
/// admin surface in this codebase already holds (e.g. <c>RotKindTokens</c>). <see cref="Version"/> is
/// also set as the response's <c>ETag</c> header (RFC 7232 weak) — carried in the body too (the
/// <c>AdminMediaDto.Version</c> precedent) so a caller already holding a list/create response can PATCH
/// or drive a verb without a separate GET just to read the header.
/// </summary>
public sealed record AdSpotDto(
    long Id,
    string Brand,
    string Title,
    string? Brief,
    string? Script,
    string Source,
    string? PackSlug,
    int SpotSeconds,
    IReadOnlyList<AdVoicePlanEntry>? VoicePlan,
    long? BedMediaId,
    string State,
    string? FailReason,
    long? MediaId,
    DateTime CreatedAt,
    DateTime StateChangedAt,
    DateTime? RenderedAt,
    DateTime? RetiredAt,
    string Version);
