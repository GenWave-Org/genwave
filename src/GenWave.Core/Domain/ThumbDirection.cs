namespace GenWave.Core.Domain;

/// <summary>
/// Direction of a spectator/operator thumb on a track's airing (SPEC F150.1, F150.7; STORY-371,
/// STORY-369; PLAN T365). Maps 1:1 to <c>library.thumb_direction</c> (<c>'up'</c>/<c>'down'</c>) by
/// lowercase text — <see cref="Garden.MediaThumbRepository"/> owns that mapping, the same explicit
/// enum-to-text discipline <c>VoteDirection</c>/<c>AnnouncementState</c> already establish for their
/// own Postgres enums/CHECK-constrained columns in this codebase. Deliberately distinct from
/// <see cref="VoteDirection"/> (SPEC F33's rating scale): a thumb writes ONLY
/// <c>library.media_thumb</c> + <c>library.media_rotation</c>, never <c>library.media_rating</c>
/// (F150.1's own disjointness) — sharing one enum across both seams would blur that boundary in code
/// even though the schema keeps it apart.
/// </summary>
public enum ThumbDirection
{
    /// <summary>A positive thumb — nudges the track's rotation score up.</summary>
    Up,

    /// <summary>A negative thumb — nudges the track's rotation score down.</summary>
    Down,
}
