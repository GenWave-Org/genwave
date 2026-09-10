using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for a show row (SPEC F115.1, F115.3, F115.4, F152.5): the whole identity package this
/// ADMIN surface edits — name/slug/tagline/flavor plus provenance (<c>importedFrom</c>/<c>importedAt</c>,
/// the db/25 pattern <see cref="PersonaDto"/> already carries) — and, since PLAN T362, the show's own
/// "deep cuts" rotation rule (<see cref="Rotation"/>). <see cref="Flavor"/> is prompt-only
/// and private FOREVER on every OTHER surface (SPEC F115.3, the persona-soul precedent) — but this
/// DTO backs the admin editor that AUTHORS it, so it is the one deliberate exception; the public/
/// spectator show projection (PLAN T251) is a separate, narrower DTO that never adds this field, and
/// never adds <see cref="Rotation"/> either — the rule is a station-operator knob, not a listener
/// fact.
/// </summary>
/// <param name="Rotation">
/// SPEC F152.3/F152.5 (STORY-373, PLAN T362) — echoed straight from <see cref="Core.Domain.Show.Rotation"/>:
/// <see langword="null"/> when the show carries no rotation rule. <see cref="ShowRotationController.SetRotation"/>
/// is the one write path (<c>PUT /api/shows/{id}</c>) — <see cref="ShowsController.Create"/>/
/// <see cref="ShowsController.Update"/> never touch it (SPEC F115.2's "unread this epic" law, narrowed
/// by exactly this one field at SPEC F152.3).
/// </param>
/// <param name="Sponsor">
/// SPEC F175.1 (STORY-430, PLAN T449) — the show's linked sponsor, or <see langword="null"/> when
/// unlinked. SPEC's own wire shape names <c>{id, name}</c>; this rides the shared
/// <see cref="SponsorRefDto"/> instead (it also carries <c>paused</c>) — the same "one cross-reference
/// shape, every non-sponsor surface embeds it" rule <c>AdBriefDto.Sponsor</c>/<c>AdSpotDto.Sponsor</c>
/// already follow (SPEC F171.6, F171.7), rather than a narrower one-off record for this field alone.
/// </param>
public sealed record ShowDto(
    long Id, string Name, string Slug, string? Tagline, string? Flavor,
    string? ImportedFrom, DateTime? ImportedAt, RotationPredicate? Rotation, SponsorRefDto? Sponsor = null)
{
    /// <summary>
    /// The one mapping from <see cref="Show"/> to this DTO (T362 review LOW-5) — shared by
    /// <see cref="ShowsController"/> (its own private <c>ToDto</c> delegates here) and
    /// <see cref="ShowRotationController"/>, so splitting the rotation routes into their own
    /// controller never risked the two drifting onto two different projections of the same row.
    /// <paramref name="sponsor"/> defaults to <see langword="null"/> (PLAN T449 ruling) —
    /// <see cref="ShowRotationController"/>'s own two call sites write only <see cref="Show.Rotation"/>
    /// and the admin-ui never merges a rotation-PUT response back into a full show row client-side
    /// (<c>lib/shows-rotation-api.ts</c> reads only <c>.rotation</c> off it), so resolving a sponsor
    /// there would add a store round trip no caller ever reads; <see cref="ShowsController"/>'s own
    /// <c>List</c>/<c>Get</c>/<c>Create</c>/<c>Update</c> actions are the only callers that pass one.
    /// </summary>
    public static ShowDto From(Show show, SponsorRefDto? sponsor = null) => new(
        show.Id, show.Name, show.Slug, show.Tagline, show.Flavor, show.ImportedFrom, show.ImportedAt,
        show.Rotation, sponsor);
}
