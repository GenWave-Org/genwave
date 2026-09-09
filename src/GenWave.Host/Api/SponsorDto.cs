namespace GenWave.Host.Api;

/// <summary>
/// The wire shape <see cref="SponsorsController"/> projects every <c>GenWave.Core.Domain.Sponsor</c>
/// row into (SPEC F171.2; STORY-407; PLAN T434) — "Host owns its own wire DTOs" (the
/// <see cref="AdSpotDto"/>/<see cref="AdBriefDto"/> precedent). No field here is ever named
/// <c>brand</c> — a sponsor is the canonical customer row itself, not the ads-side <c>brand</c> bridge
/// <see cref="AdSpotDto"/> still carries (PLAN T432's compile bridge, retired at T436).
/// </summary>
/// <param name="Id">The row's own surrogate key.</param>
/// <param name="Name">The sponsor's display name.</param>
/// <param name="PackSlug">The installing pack's slug, or <see langword="null"/> for an owner-created
/// sponsor.</param>
/// <param name="Paused">Whether this sponsor's spots are currently withheld from airing.</param>
/// <param name="PausedAt">When <see cref="Paused"/> was last set <see langword="true"/>, or
/// <see langword="null"/> when not currently paused.</param>
/// <param name="Tagline">A short pitch line, or <see langword="null"/>.</param>
/// <param name="About">A longer description, or <see langword="null"/>.</param>
/// <param name="Phone">A contact phone number, or <see langword="null"/>.</param>
/// <param name="Address">A physical address, or <see langword="null"/>.</param>
/// <param name="Website">A <c>http(s)://</c> URL, or <see langword="null"/>.</param>
/// <param name="Tone">A house tone hint, or <see langword="null"/>.</param>
/// <param name="CreatedAt">When this sponsor was first created.</param>
/// <param name="UpdatedAt">When this row was last written.</param>
public sealed record SponsorDto(
    long Id,
    string Name,
    string? PackSlug,
    bool Paused,
    DateTime? PausedAt,
    string? Tagline,
    string? About,
    string? Phone,
    string? Address,
    string? Website,
    string? Tone,
    DateTime CreatedAt,
    DateTime UpdatedAt);
