namespace GenWave.Ads;

/// <summary>
/// The outcome of <see cref="AdSpotJobService.TryEnqueueAsync"/> (SPEC F174.2, F174.3; STORY-422,
/// STORY-423; PLAN T441) — every distinct answer an operator's <c>POST /api/ads/{id}/write</c> (or
/// T442's <c>preview</c>) call can receive, mapped 1:1 by <c>AdsController</c> to a status code.
/// </summary>
public enum AdSpotJobEnqueueResult
{
    /// <summary>The row was stamped and the job was written to the queue — a caller re-reads the
    /// row to see the fresh <c>job</c> object.</summary>
    Accepted,

    /// <summary>The row already carries a <c>job_kind</c> — some job (this kind or another) is
    /// already queued or running for this id. Maps to 409 <c>ad_job_busy</c>.</summary>
    Busy,

    /// <summary>The row was stamped, but the station-wide queue was already at
    /// <see cref="AdsOptions.JobQueueCapacity"/> — the stamp is undone before this is returned, so
    /// the row is left exactly as it was found. Maps to 429 <c>ad_job_queue_full</c>.</summary>
    QueueFull,

    /// <summary>No row exists with the given id. Maps to 404.</summary>
    NotFound,
}
