using GenWave.Core.Domain;

namespace GenWave.Core.Events;

/// <summary>
/// A new stamped item came on-air (real track-id advance detected by the feeder). Carries the same
/// Core-friendly primitives the retired <c>PlayoutFeeder.OnAdvance</c> single-cast callback did —
/// this event is that signal, multicast-capable (gitea-#246). <paramref name="DurationMs"/> carries
/// <c>tts:*</c> patter's measured cue-derived duration (SPEC F66.1); it is null only for an
/// engine-initiated advance, which the Host rehydrates from the catalog after publish (SPEC F66.2).
/// </summary>
/// <param name="MediaId">The catalog id of the item now on-air.</param>
/// <param name="Title">The item's title, when known.</param>
/// <param name="Artist">The item's artist, when known.</param>
/// <param name="GainDb">The loudness-match gain applied for this airing.</param>
/// <param name="StartedAt">The genuine air-time instant this event was published.</param>
/// <param name="DurationMs">The item's measured cue-derived duration, or <see langword="null"/> for
/// an engine-initiated advance (see the type-level remarks).</param>
/// <param name="PersonaPick">
/// SPEC F82.6, F83.1, F86.1 (STORY-217, PLAN T73) — the SAME <see cref="PersonaPickDiagnostics"/> the
/// copywriter reads off <c>MediaItem.PersonaPick</c> (no re-derivation), carried straight from
/// <c>GenWave.Core.Playout.PlayoutFeeder</c>'s own pushed-item metadata at the instant this
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
    SegmentKind? SegmentKind = null) : StationEvent
{
    /// <summary>
    /// SPEC F127.11 (STORY-329, PLAN T287) — the SAME <see cref="CrosstalkAiredScript"/> the
    /// feeder's pushed-item metadata carries off <c>MediaItem.CrosstalkScript</c>, forwarded the same way
    /// <see cref="PersonaPick"/> is above. Non-null only when <see cref="SegmentKind"/> is
    /// <see cref="Core.Domain.SegmentKind.Crosstalk"/>. The booth log's event consumer stamps this SAME
    /// row's <c>pick</c> jsonb from exactly this value (the <c>BoothLogPickStamp</c> precedent, narrowed
    /// per-kind) rather than a persona-pick stamp.
    ///
    /// <para>
    /// <b>Declared as a defaulted body property, not a 9th primary-constructor parameter (round-2
    /// review F1 — the exact T285-round-3 defect, <see cref="Core.Domain.ShowSummary.Slug"/>'s own
    /// precedent).</b> This record already shipped inside the Abstractions 5.0.0 NuGet with an 8-arg
    /// <c>ctor</c> and 8-arity <c>Deconstruct</c>; adding a further positional parameter would silently
    /// delete both from the published binary surface, breaking every compiled caller regardless of the
    /// new parameter's own default value. Every construction site that needs to set this uses an
    /// object-initializer/<c>with</c> expression, never a positional/named constructor argument.
    /// </para>
    /// </summary>
    public CrosstalkAiredScript? CrosstalkScript { get; init; }

    /// <summary>
    /// SPEC F152.4 (STORY-372, PLAN T361) — the SAME <see cref="RotationCandidate.RotationRelax"/> the
    /// feeder's pushed-item metadata carries off <c>MediaItem.RotationRelax</c>, forwarded the same way
    /// <see cref="CrosstalkScript"/> is above. <see langword="null"/> for every engine-initiated advance
    /// (the feeder never pushed this id, so nothing was ever stamped) and for every pick whose envelope
    /// carried no rotation predicate at all. The booth log's event consumer stamps
    /// <c>station.booth_log.pick</c>'s <c>rotationRelax</c> member from exactly this value (SPEC F86.1's
    /// <c>BoothLogPickStamp</c>, additive) — omitted entirely, never <c>0</c>, when this is null
    /// (STORY-372 AC10).
    ///
    /// <para>
    /// A defaulted body property, not a positional constructor parameter — same binary-compatibility
    /// discipline as <see cref="CrosstalkScript"/>'s own remarks.
    /// </para>
    /// </summary>
    public int? RotationRelax { get; init; }
}
