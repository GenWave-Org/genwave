namespace GenWave.Core.Domain;

/// <summary>
/// The outcome of an xmin-guarded <c>station.ad_spot</c> transition
/// (<c>Abstractions.IAdSpotStore</c>'s <c>ApproveAsync</c>/<c>RetryAsync</c>/<c>RetireAsync</c> —
/// SPEC F159.2; STORY-389; PLAN T398) — mirrors <see cref="MediaWriteResult"/>'s own three-outcome
/// shape one table over.
/// </summary>
public enum AdSpotWriteResult
{
    /// <summary>The transition applied — <see cref="AdSpotTransitionOutcome.Spot"/> carries the
    /// row's fresh state and <see cref="AdSpot.Version"/>.</summary>
    Updated,

    /// <summary>No row exists with the given id — existence is checked FIRST (the
    /// <c>Catalog.MediaRepository.UpdateCoreAsync</c> precedent): IDOR-safe, an unknown id always
    /// reports this outcome, never a signal that would let a caller distinguish "stale version/
    /// illegal state" from "doesn't exist".</summary>
    NotFound,

    /// <summary>The row exists but the guarded <c>WHERE</c> didn't match — either the caller's own
    /// <c>expectedVersion</c> is stale, or the row is no longer in the state this transition requires
    /// (an illegal move, SPEC F159.2, STORY-389 AC6). Both collapse to one outcome: either way, the
    /// caller's view of the row is out of date and must re-read before trying again.</summary>
    Conflict,
}
