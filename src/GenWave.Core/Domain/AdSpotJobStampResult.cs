namespace GenWave.Core.Domain;

/// <summary>
/// The outcome of <c>Abstractions.IAdSpotStore.StampJobAsync</c> (SPEC F174, F175; STORY-406; PLAN
/// T432) — mirrors <see cref="AdSpotWriteResult"/>'s own three-outcome enum shape one seam over.
/// </summary>
public enum AdSpotJobStampResult
{
    /// <summary>The claim applied — <c>job_kind</c>/<c>job_started_at</c> now belong to the caller,
    /// <c>job_error</c> is cleared.</summary>
    Stamped,

    /// <summary>The row already carries a non-null <c>job_kind</c> — another job holds it; the caller
    /// backs off rather than stealing the claim.</summary>
    Busy,

    /// <summary>No row exists with the given id.</summary>
    NotFound,
}
