namespace GenWave.Host.Api;

/// <summary>
/// The wire shape of an <c>ad_spot</c> row's in-flight job (SPEC F174.2, F174.3; STORY-422 AC4/AC7,
/// STORY-423 AC2/AC4, STORY-435; PLAN T441, T464) — <see cref="AdSpotDto.Job"/> is
/// <see langword="null"/> exactly when the row carries none of <c>job_kind</c>, <c>job_error</c>, or
/// <c>job_failed_kind</c>; otherwise this carries whichever of the three is set (PLAN T441 ruling: a
/// failed job's own <see cref="Error"/> stays visible with <see cref="Kind"/>/<see cref="StartedAt"/>
/// both <see langword="null"/>, since <c>IAdSpotStore.ClearJobAsync</c> clears
/// <c>job_kind</c>/<c>job_started_at</c> on every outcome, success or failure alike, and sets
/// <c>job_error</c>/<c>job_failed_kind</c> on every outcome too — <see langword="null"/> on a clean
/// finish, the message and the failing kind on failure).
/// </summary>
/// <param name="Kind">The job's own kind (<c>"write"</c> or <c>"preview"</c>), or
/// <see langword="null"/> once the job has finished (success or failure) or was never started.</param>
/// <param name="FailedKind">The kind (<c>"write"</c>/<c>"preview"</c>) of the most recent job that
/// FAILED, or <see langword="null"/> while a job is running or after a clean finish (STORY-435 gh-#724,
/// PLAN T464) — unlike <see cref="Kind"/>, this survives the job's own <c>ClearJobAsync</c> clear on
/// failure, so the wizard can route a stale error back to the step that actually produced it.</param>
/// <param name="StartedAt">When <c>IAdSpotStore.StampJobAsync</c> claimed the row, or
/// <see langword="null"/> once the job has finished or was never started.</param>
/// <param name="WaitingForStation">Whether <see cref="Kind"/>'s job is right now blocked on
/// <c>IOnAirRenderSignal.InFlight</c> — purely <c>AdSpotJobService</c>'s own in-memory runner state,
/// never a database column, so this reads <see langword="false"/> for a job this process does not
/// currently hold (including a stamped-but-orphaned row after a restart).</param>
/// <param name="Error">The most recent job failure's reason, or <see langword="null"/> when the row's
/// last job (if any) succeeded or none has ever run.</param>
public sealed record AdSpotJobDto(string? Kind, string? FailedKind, DateTime? StartedAt, bool WaitingForStation, string? Error);
