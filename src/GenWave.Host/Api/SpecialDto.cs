namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for a persisted <see cref="GenWave.Core.Domain.ScheduleSpecial"/> row (SPEC F120.1/
/// F120.3, STORY-317, PLAN T259) — <c>GET /api/schedule/specials</c>'s list rows and
/// <c>POST /api/schedule/specials</c>'s 201 body both use this shape. <see cref="ShowId"/> is the
/// bare foreign key, never a nested show summary — mirrors <see cref="ScheduleSegmentDto"/>'s own
/// posture (that DTO carries no nested show object either): the Schedule page already loads the full
/// show roster once, server-side (PLAN T245), so a client resolves a name from
/// <see cref="ShowId"/> locally rather than this endpoint fabricating a second projection of the same
/// row on every list read.
/// </summary>
public sealed record SpecialDto(
    long Id,
    DateOnly OnDate,
    int StartMinute,
    int EndMinute,
    long? PersonaId,
    IReadOnlyList<string>? Genres,
    double? EnergyMin,
    double? EnergyMax,
    long? ShowId);
