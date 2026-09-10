namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /api/sponsors</c>' own row shape (SPEC F171.2; STORY-407; PLAN T434) — every
/// <see cref="SponsorDto"/> fact plus the referencing counts
/// <c>GenWave.Core.Domain.SponsorListRow</c> (<c>Abstractions.ISponsorStore.ListAsync</c>) already
/// computes alongside the list itself, so a caller never needs a second round trip per row just to
/// show "2 briefs, 5 spots, 1 show" next to a name.
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
/// <param name="Briefs">How many <c>station.ad_brief</c> rows name this sponsor.</param>
/// <param name="Spots">How many <c>station.ad_spot</c> rows name this sponsor, grouped by state —
/// keyed by the SAME lowercase wire tokens <c>AdStateTokens</c> already fixes for
/// <see cref="AdSpotDto.State"/> (<c>draft</c>/<c>approved</c>/<c>rendering</c>/<c>ready</c>/
/// <c>failed</c>/<c>retired</c>); a state with zero spots is simply absent from the map, never an
/// explicit zero entry (the <c>SponsorListRow.SpotsByState</c> precedent this projects).</param>
/// <param name="Shows">How many <c>station.show</c> rows name this sponsor.</param>
public sealed record SponsorListItemDto(
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
    DateTime UpdatedAt,
    int Briefs,
    IReadOnlyDictionary<string, int> Spots,
    int Shows);
