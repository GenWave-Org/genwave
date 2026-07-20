namespace GenWave.MediaLibrary.Station;

/// <summary>
/// One narrative row queued for <c>station.booth_log</c> (SPEC F72.1, STORY-195) —
/// <see cref="BoothLogWriter"/>'s enqueue payload, drained by <see cref="BoothLogDrainService"/>.
/// Internal plumbing only; the public shape read back is <see cref="GenWave.Core.Domain.BoothLogEntry"/>,
/// which additionally carries the DB-assigned id/occurred_at.
/// </summary>
sealed record BoothLogEntryRequest(string Kind, string Summary);
