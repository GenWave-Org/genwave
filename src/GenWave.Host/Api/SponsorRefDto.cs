using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The small sponsor cross-reference every non-sponsor admin surface embeds on ITS OWN rows (SPEC
/// F171.6, F171.7; STORY-411, STORY-412; PLAN T435, T436) — <c>AdBriefDto.Sponsor</c> and
/// <c>AdSpotDto.Sponsor</c> both project through <see cref="From"/> below. Deliberately NOT
/// <see cref="SponsorDto"/>: a brief or a spot names its sponsor, it does not describe one —
/// <see cref="Id"/>/<see cref="Name"/> for display and <see cref="Paused"/> so a caller can grey out a
/// paused sponsor's rows without a second round trip to <c>GET /api/sponsors/{id}</c>. No field here is
/// ever <c>brand</c>.
/// </summary>
/// <param name="Id">The sponsor's own surrogate key.</param>
/// <param name="Name">The sponsor's display name.</param>
/// <param name="Paused">Whether this sponsor's spots are currently withheld from airing.</param>
public sealed record SponsorRefDto(long Id, string Name, bool Paused)
{
    /// <summary>
    /// The one <see cref="Sponsor"/> → <see cref="SponsorRefDto"/> projection (T436 review finding: was
    /// two live copies, <c>AdBriefsController.ToSponsorDto</c> and an about-to-be-added
    /// <c>AdsController</c> twin — hoisted here instead so both controllers call the SAME mapping).
    /// </summary>
    public static SponsorRefDto From(Sponsor sponsor) => new(sponsor.Id, sponsor.Name, sponsor.Paused);
}
