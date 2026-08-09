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
///
/// <see cref="Pick"/> (SPEC F86.1, STORY-217, PLAN T73) rides the SAME capture-at-publish-time
/// discipline: <see cref="BoothLogWriter.Publish"/> reads it straight off the <see cref="TrackAired"/>
/// event's own <c>PersonaPick</c> — which <c>PlayoutFeeder</c> already captured synchronously at push
/// time — and pre-serializes it to the exact F86.1 jsonb text (or <see langword="null"/>) before this
/// request ever reaches the queue, so <see cref="BoothLogDrainService"/> persists it verbatim like
/// every other field here.
///
/// <see cref="MediaId"/> (gh-#99) rides the same track-start-only path: the aired catalog row's
/// numeric id, parsed from <see cref="TrackAired"/>'s own media id at publish time —
/// <see langword="null"/> for every other kind or a non-catalog id. It is what lets the Host answer
/// "was this airing safe-scope content?" for the taste-thumb exclusion, since <c>station.booth_log</c>
/// itself can never join <c>library.media</c> (schema-role boundary).
///
/// <see cref="SegmentKind"/> (SPEC F113.1, STORY-304, PLAN T220) rides the same track-start-only,
/// captured-at-publish-time path: <see cref="BoothLogWriter.Publish"/> reads it straight off the
/// <see cref="TrackAired"/> event's own <c>SegmentKind</c> — which <c>PlayoutFeeder</c> already
/// forwarded from the airing <c>MediaItem</c> — and stringifies it (the enum's token name, e.g.
/// <c>"StationId"</c>) before this request ever reaches the queue. <see langword="null"/> for a music
/// row or an engine-initiated advance — the demo-hour instrument's whole point is that this is the
/// genuine AIR-time signal, never a render-time guess.
/// </summary>
sealed record BoothLogEntryRequest(
    string Kind, string Summary, long? PersonaId, string? Artist = null, string? Pick = null, long? MediaId = null,
    string? SegmentKind = null);
