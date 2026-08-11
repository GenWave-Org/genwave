namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.booth_log</c> row awaiting persistence — <see cref="Abstractions.IBoothLogAppender.AppendAsync"/>'s
/// parameter object (PLAN T220 review carry-forward: "IBoothLogAppender 8-param positional call wants
/// a Core-side parameter object"; closed here at PLAN T242, where <see cref="ShowId"/> would have
/// pushed that positional call to nine). Every field mirrors a stamp <c>BoothLogWriter.Publish</c>
/// already captured SYNCHRONOUSLY, at AIR time, before the request was ever queued for
/// <c>BoothLogDrainService</c> to drain — this type only regroups them into one argument; it derives
/// nothing of its own and enforces no invariant beyond the shape.
/// </summary>
/// <param name="Kind">The narrative kind (e.g. <c>"track-started"</c>, <c>"patter-aired"</c>).</param>
/// <param name="Summary">The operator-readable narrative line — human language, never a JSON dump.</param>
/// <param name="PersonaId">SPEC F84.6, STORY-215 — the persona on air at write time for a
/// TRACK-START row. <see langword="null"/> for every other kind, or a persona-less airing.</param>
/// <param name="Artist">SPEC F84.1, STORY-215, PLAN T70 — that same track's artist, captured the same
/// way and for the same reason: the accrual write path needs a STRUCTURED artist to build an
/// artist-predicate rule from, never a regex over <see cref="Summary"/>'s narrative prose.
/// <see langword="null"/> for every non-track row or a track aired with no known artist.</param>
/// <param name="Pick">SPEC F86.1, STORY-217, PLAN T73 — that same track's persona-pick stamp, the
/// caller's already-serialized jsonb text, or <see langword="null"/> for every non-track row, an
/// engine-initiated play, or a persona-off pick. Never backfilled.</param>
/// <param name="MediaId">gh-#99 — the aired row's numeric catalog id, or <see langword="null"/> for
/// every non-track row or a non-catalog id.</param>
/// <param name="SegmentKind">SPEC F113.1, STORY-304, PLAN T220 — that same track's air-time
/// <c>SegmentKind</c> token name (e.g. <c>"StationId"</c>), or <see langword="null"/> for a music row
/// or a non-track row.</param>
/// <param name="ShowId">SPEC F121.1, STORY-310, PLAN T242 — the show on air at write time for a
/// TRACK-START row, music and kinded alike, captured the SAME way and for the SAME reason as
/// <paramref name="PersonaId"/>: the resolver's on-air answer at the exact instant the row aired,
/// never re-derived later. <see langword="null"/> for every non-track row or a showless airing. No
/// FK — history must outlive the entity, so a show deleted later never rewrites or blocks on a past
/// airing.</param>
public sealed record BoothLogAppendRequest(
    string Kind, string Summary, long? PersonaId, string? Artist, string? Pick, long? MediaId,
    string? SegmentKind, long? ShowId);
