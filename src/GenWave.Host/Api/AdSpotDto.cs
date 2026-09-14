using GenWave.Ads;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The wire shape <see cref="AdsController"/> projects every <c>GenWave.Core.Domain.AdSpot</c> row
/// into (SPEC F162.1, F171.7; STORY-392, STORY-412; PLAN T403, T436) — "Host owns its own wire DTOs"
/// (see <c>AnnouncementHistoryDto</c>'s own remarks for the house convention), so a future Core-side
/// field never leaks onto the wire without a deliberate projection change here. <see cref="Source"/>/
/// <see cref="State"/> ride as their own snake_case wire tokens (<c>AdSourceTokens</c>/
/// <c>AdStateTokens</c>) — the same "enum as a stable string, never the C# name" posture every other
/// admin surface in this codebase already holds (e.g. <c>RotKindTokens</c>). <see cref="Version"/> is
/// also set as the response's <c>ETag</c> header (RFC 7232 weak) — carried in the body too (the
/// <c>AdminMediaDto.Version</c> precedent) so a caller already holding a list/create response can PATCH
/// or drive a verb without a separate GET just to read the header. <see cref="SponsorName"/> is the
/// created-at/refreshed-on-change snapshot <c>station.ad_spot.sponsor_name</c> already carries (SPEC
/// F171.7); <see cref="Sponsor"/> is the live <see cref="SponsorRefDto"/> cross-reference (the
/// <see cref="AdBriefDto.Sponsor"/> precedent one seam over) — a sponsor is always referenced by its
/// own id/name/paused, never by a free-text customer label field. <see cref="Job"/> (PLAN T441) is
/// <see langword="null"/> exactly when the row carries neither a job stamp nor a job error — see
/// <see cref="AdSpotJobDto"/>'s own remarks for the shape when it is not. <see cref="Preview"/> (SPEC
/// F174.4; PLAN T442) is <see langword="null"/> exactly when no preview has ever been rendered for
/// this row — see <see cref="AdSpotPreviewDto"/>'s own remarks for the shape when it is not.
/// <see cref="RenderWithinMinutes"/> (STORY-433; PLAN T457; gh-#745) is the configured render pass
/// cadence (<c>Ads:WorkerIntervalMinutes</c>) for a spot in the <see cref="AdState.Approved"/> state,
/// and <see langword="null"/> for every other state — the UI uses it to tell the operator roughly when
/// the spot will be picked up, not a guaranteed bound.
/// </summary>
public sealed record AdSpotDto(
    long Id,
    long SponsorId,
    string SponsorName,
    SponsorRefDto Sponsor,
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
    string Version,
    AdSpotJobDto? Job,
    AdSpotPreviewDto? Preview,
    int? RenderWithinMinutes);
