namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/booth-log</c> (SPEC F72.2): an operator-readable narrative entry.
/// <see cref="PersonaId"/> (SPEC F84.6, STORY-215) is the persona stamped on air for a track-start
/// row — <see langword="null"/> for every other kind, a persona-less airing, or a row that predates
/// the column. The admin UI (T71) uses its presence to decide whether a row is thumbable for taste.
/// </summary>
public sealed record BoothLogEntryDto(DateTime OccurredAt, string Kind, string Summary, long? PersonaId);
