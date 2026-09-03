using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The wire shape <see cref="AdBriefsController"/> projects every <see cref="AdBrief"/> row into (SPEC
/// F162.1, F162.2; STORY-392; PLAN T403b) — "Host owns its own wire DTOs" (the
/// <see cref="AdSpotDto"/> precedent one controller over). <see cref="PackSlug"/>
/// <see langword="null"/> means an owner-authored brief (the same "null pack_slug = owner" reading
/// <see cref="AdBrief"/>'s own remarks give).
/// </summary>
public sealed record AdBriefDto(
    long Id,
    string? PackSlug,
    string Brand,
    string? Premise,
    string? Tone,
    string? Structure,
    bool Enabled,
    DateTime CreatedAt);
