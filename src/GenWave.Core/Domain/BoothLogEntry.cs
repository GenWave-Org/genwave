namespace GenWave.Core.Domain;

/// <summary>
/// One narrative row read back from <c>station.booth_log</c> (SPEC F72.1-F72.3, STORY-195) — the
/// operator-readable "what the DJ did and said" feed: track starts, patter airs, degradation mode
/// changes. <see cref="Summary"/> is always human language, never a JSON dump.
///
/// <see cref="PersonaId"/> (SPEC F84.6, STORY-215) is the persona on air when a TRACK-START row
/// aired, stamped at write time — <see langword="null"/> for every non-track row, a persona-less
/// airing, or a row that predates the column. Defaults to <see langword="null"/> so every existing
/// four-argument construction (tests, fixtures) stays additive/unstamped without changes.
///
/// <see cref="Pick"/> (SPEC F86.1, F86.2; STORY-217, PLAN T73/T74) is the row's stored
/// <c>booth_log.pick</c> jsonb, verbatim as text — the caller (currently only
/// <c>BoothLogController</c>) deserializes it through <see cref="BoothLogPickStampSerializer"/>
/// rather than this type doing so itself; a plain-text passthrough, same as every other jsonb column
/// this codebase's Dapper rows carry. <see langword="null"/> for every non-track row, an
/// engine-initiated play, a persona-off pick, or a row that predates the column.
/// </summary>
public sealed record BoothLogEntry(
    long Id, DateTime OccurredAt, string Kind, string Summary, long? PersonaId = null, string? Pick = null);
