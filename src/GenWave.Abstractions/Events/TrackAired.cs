using GenWave.Core.Domain;

namespace GenWave.Core.Events;

/// <summary>
/// A new stamped item came on-air (real track-id advance detected by the feeder). Carries the same
/// Core-friendly primitives the retired <c>PlayoutFeeder.OnAdvance</c> single-cast callback did —
/// this event is that signal, multicast-capable (gitea-#246). <paramref name="DurationMs"/> carries
/// <c>tts:*</c> patter's measured cue-derived duration (SPEC F66.1); it is null only for an
/// engine-initiated advance, which the Host rehydrates from the catalog after publish (SPEC F66.2).
/// </summary>
/// <param name="PersonaPick">
/// SPEC F82.6, F83.1, F86.1 (STORY-217, PLAN T73) — the SAME <see cref="PersonaPickDiagnostics"/> the
/// copywriter reads off <c>MediaItem.PersonaPick</c> (no re-derivation), carried straight from
/// <see cref="GenWave.Core.Playout.PlayoutFeeder"/>'s own pushed-item metadata at the instant this
/// event is published — one source of truth for both consumers. <see langword="null"/> for every
/// engine-initiated advance (the feeder never pushed this id, so it never held a pick) and for the
/// common persona-off case. The booth log's own event consumer stamps <c>station.booth_log.pick</c>
/// from exactly this value.
/// </param>
/// <param name="SegmentKind">
/// SPEC F113.1 (STORY-304, PLAN T220) — the demo-hour observability instrument: the SAME
/// <see cref="SegmentKind"/> the feeder's pushed-item metadata carries off <c>MediaItem.SegmentKind</c>,
/// forwarded the same way <paramref name="PersonaPick"/> is above. <see langword="null"/> for every
/// music row and every engine-initiated advance (the feeder never pushed it, so no kind was ever
/// stamped). The booth log's event consumer stamps <c>station.booth_log.segment_kind</c> from exactly
/// this value, at the genuine AIR-time instant this event is published — never at render time.
/// </param>
public sealed record TrackAired(
    string MediaId,
    string? Title,
    string? Artist,
    double GainDb,
    DateTimeOffset StartedAt,
    int? DurationMs,
    PersonaPickDiagnostics? PersonaPick = null,
    SegmentKind? SegmentKind = null) : StationEvent;
