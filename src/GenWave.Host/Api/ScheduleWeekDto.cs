namespace GenWave.Host.Api;

/// <summary>
/// The whole-week document body shared by <c>GET /api/schedule</c> (the current grid) and
/// <c>PUT /api/schedule</c> (the replacement grid) — SPEC F91.1, F91.8; STORY-240, PLAN T122.
/// A week with zero <see cref="Segments"/> is legal on both verbs: the pre-clock, no-active-persona,
/// 24/7-music-only state (SPEC F91.4).
/// </summary>
public sealed record ScheduleWeekDto(IReadOnlyList<ScheduleSegmentDto> Segments);
