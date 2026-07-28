namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IMediaExplicitOverride.SetExplicitOverrideAsync"/> (SPEC
/// F95.3, STORY-251, PLAN T115). Deliberately smaller than <see cref="MediaWriteResult"/>: an
/// operator override is never scope-gated and carries no <c>expectedVersion</c> to conflict on (no
/// <c>If-Match</c> anywhere in this seam, mirroring <see cref="RatingWriteResult"/>) — a set or
/// clear can only fail on a missing row.
/// </summary>
public enum ExplicitOverrideResult
{
    /// <summary>The media row exists and the override was applied (set or cleared).</summary>
    Updated,

    /// <summary>No row with the given media id exists in <c>library.media</c>.</summary>
    NotFound,
}
