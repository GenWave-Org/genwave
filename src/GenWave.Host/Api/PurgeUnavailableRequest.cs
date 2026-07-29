namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/media/purge-unavailable</c> (gh-#113).
///
/// <see cref="OlderThanDays"/> — minimum whole days a row must have been unavailable (per its
/// <c>unavailable_since</c> stamp) to be purged. Absent/null reads as the default (7); a value
/// below 1 is rejected with 400 before anything is counted or deleted.
///
/// <see cref="DryRun"/> — true counts the rows a purge would delete without deleting anything;
/// the Admin UI fetches this figure first so its confirm dialog can NAME the count before the
/// destructive call. The tripwire (candidates exceeding half the library → 409) fires in both
/// modes, so a dry run already surfaces the refusal an actual purge would hit.
/// </summary>
public sealed record PurgeUnavailableRequest(int? OlderThanDays = null, bool DryRun = false);
