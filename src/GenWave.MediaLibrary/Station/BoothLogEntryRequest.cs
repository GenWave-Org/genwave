namespace GenWave.MediaLibrary.Station;

/// <summary>
/// One narrative row queued for <c>station.booth_log</c> (SPEC F72.1, STORY-195) —
/// <see cref="BoothLogWriter"/>'s enqueue payload, drained by <see cref="BoothLogDrainService"/>.
/// Internal plumbing only; the public shape read back is <see cref="GenWave.Core.Domain.BoothLogEntry"/>,
/// which additionally carries the DB-assigned id/occurred_at.
///
/// <see cref="PersonaId"/> (SPEC F84.6, STORY-215) is captured SYNCHRONOUSLY by
/// <see cref="BoothLogWriter.Publish"/> at air time — a track-start row carries whichever persona
/// was active at that exact instant; every other kind, and a persona-less airing, carries
/// <see langword="null"/>. <see cref="BoothLogDrainService"/> persists this value verbatim; it never
/// re-resolves the active persona itself. This is deliberate: the bounded queue between publish and
/// drain can back up (a DB outage/backlog), and resolving at drain time would mis-stamp an
/// already-queued row with whatever persona is active by the time the backlog clears, not the one
/// that was actually on air when the row was created.
/// </summary>
sealed record BoothLogEntryRequest(string Kind, string Summary, long? PersonaId);
