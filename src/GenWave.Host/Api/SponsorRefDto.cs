namespace GenWave.Host.Api;

/// <summary>
/// The small sponsor cross-reference every non-sponsor admin surface embeds on ITS OWN rows (SPEC
/// F171.6, F171.7; STORY-411; PLAN T435) — <c>AdBriefDto.Sponsor</c> here, <c>AdSpotDto.Sponsor</c> at
/// T436 one seam over. Deliberately NOT <see cref="SponsorDto"/>: a brief or a spot names its sponsor,
/// it does not describe one — <see cref="Id"/>/<see cref="Name"/> for display and
/// <see cref="Paused"/> so a caller can grey out a paused sponsor's rows without a second round trip to
/// <c>GET /api/sponsors/{id}</c>. No field here is ever <c>brand</c>.
/// </summary>
/// <param name="Id">The sponsor's own surrogate key.</param>
/// <param name="Name">The sponsor's display name.</param>
/// <param name="Paused">Whether this sponsor's spots are currently withheld from airing.</param>
public sealed record SponsorRefDto(long Id, string Name, bool Paused);
