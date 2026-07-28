namespace GenWave.Abstractions.Playout;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F91.3 (STORY-241, PLAN T119) — the answer to "who/what is on the air right now," produced by
/// <c>GenWave.Orchestration.ScheduleResolver</c> from a station-local wall-clock instant and a
/// <see cref="GenWave.Core.Domain.ScheduleWeekSnapshot"/>. Lives in <c>GenWave.Abstractions.Playout</c>
/// next to <see cref="SegmentEnvelope"/> for the same reason that type does: both are consumed across
/// <c>GenWave.Orchestration</c> and <c>GenWave.Host</c>, never behind a single module's own internal
/// wall.
/// </summary>
/// <param name="Segment">The schedule row on air right now, or <see langword="null"/> for a grid gap
/// (SPEC F91.4) — an empty grid resolves every instant to a gap.</param>
/// <param name="PersonaId">The on-air persona, or <see langword="null"/> for a gap (music-only) —
/// always exactly <see cref="Segment"/>'s own <c>PersonaId</c>, carried here so a caller never has to
/// null-check <see cref="Segment"/> first just to learn who is on.</param>
/// <param name="Envelope">The effective envelope for this instant: <see cref="Segment"/>'s own
/// genres/energy where set, the station-default value for any field the segment leaves NULL (SPEC
/// F91.4), or the station-default envelope wholesale during a gap.</param>
/// <param name="BoundaryAt">The next wall-clock instant at which the resolved <see cref="Segment"/>/gap
/// changes — the moment a caller holding this snapshot goes stale — or <see langword="null"/> when the
/// grid is empty (a 24/7 gap has no boundary, ever). Row-accurate by ruling (SPEC F92.3, recorded at
/// build/T119 review): reported even when <see cref="Segment"/> and <see cref="NextSegment"/> share the
/// SAME <c>PersonaId</c> — e.g. the F91.6 seeded grid (seven all-day rows, one DJ) rolling from
/// Saturday's row into Sunday's at midnight. This type never dedupes a same-persona adjacency; the
/// handoff ceremony producer (PLAN T124) is the one place that decides no ceremony airs for it.</param>
/// <param name="NextSegment">The segment on air immediately after <see cref="BoundaryAt"/>, or
/// <see langword="null"/> when a gap follows. Same F92.3 ruling as <see cref="BoundaryAt"/>: a
/// same-persona successor (e.g. the F91.6 seeded grid's own midnight roll) is still reported here, never
/// collapsed away just because it will air no handoff ceremony.</param>
public sealed record OnAirSnapshot(
    ScheduleSegment? Segment,
    long? PersonaId,
    SegmentEnvelope Envelope,
    DateTimeOffset? BoundaryAt,
    ScheduleSegment? NextSegment);
