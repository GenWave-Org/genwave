namespace GenWave.Host.Api;

/// <summary>
/// The whole-week document body shared by <c>GET /api/schedule</c> (the current grid) and
/// <c>PUT /api/schedule</c> (the replacement grid) — SPEC F91.1, F91.8; STORY-240, PLAN T122.
/// A week with zero <see cref="Segments"/> is legal on both verbs: the pre-clock, no-active-persona,
/// 24/7-music-only state (SPEC F91.4).
///
/// <para>
/// Optimistic-concurrency pair (gh-#255): <see cref="Version"/> is the stored week's
/// <see cref="GenWave.Core.Domain.ScheduleWeekVersion"/> content fingerprint — populated on every
/// document the SERVER sends (GET and a PUT's 200 body), ignored if a client echoes it in a PUT.
/// <see cref="BaseVersion"/> travels the other way: a PUT carries the <see cref="Version"/> the
/// editor originally loaded, and the store 409s the replace when the stored week no longer matches —
/// a full-replace built from stale state silently destroys someone else's saved work otherwise.
/// <see langword="null"/> <see cref="BaseVersion"/> skips the check (legacy clients); the server
/// never populates it.
/// </para>
/// </summary>
public sealed record ScheduleWeekDto(
    IReadOnlyList<ScheduleSegmentDto> Segments,
    string? Version = null,
    string? BaseVersion = null);
