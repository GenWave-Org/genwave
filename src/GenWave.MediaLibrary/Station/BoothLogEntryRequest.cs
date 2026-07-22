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
///
/// <see cref="Artist"/> (SPEC F84.1, STORY-215, PLAN T70) rides the same track-start-only, captured-
/// at-publish-time path as <see cref="PersonaId"/> — the accrual write path needs a structured artist
/// to build an artist-predicate rule from, never a regex over <see cref="Summary"/>'s narrative prose.
/// <see langword="null"/> for every other kind, or a track aired with no known artist.
/// </summary>
sealed record BoothLogEntryRequest(string Kind, string Summary, long? PersonaId, string? Artist = null);
