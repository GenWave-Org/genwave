using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The wire shape <see cref="AdBriefsController"/> projects every <see cref="AdBrief"/> row into (SPEC
/// F171.6; STORY-411; PLAN T435) — "Host owns its own wire DTOs" (the <see cref="AdSpotDto"/>
/// precedent). <see cref="PackSlug"/> <see langword="null"/> means an owner-authored brief (the same
/// "null pack_slug = owner" reading <see cref="AdBrief"/>'s own remarks give). <see cref="Sponsor"/>
/// carries the sponsor this brief belongs to (<see cref="SponsorRefDto"/>) — there is no top-level
/// free-text company-name field anywhere on this shape; a brief's advertiser IS its sponsor's <c>name</c>.
/// </summary>
public sealed record AdBriefDto(
    long Id,
    string? PackSlug,
    SponsorRefDto Sponsor,
    string? Premise,
    string? Tone,
    string? Structure,
    bool Enabled,
    DateTime CreatedAt);
