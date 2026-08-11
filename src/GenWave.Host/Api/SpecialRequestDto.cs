namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/schedule/specials</c> (SPEC F120.1/F120.3, STORY-317, PLAN T259) —
/// the wire shape of one draft <see cref="GenWave.Core.Domain.ScheduleSpecial"/> row. Mirrors
/// <see cref="ScheduleSegmentDto"/>'s own field set with exactly one substitution
/// (<see cref="OnDate"/> replaces <c>Day</c> — <see cref="GenWave.Core.Domain.ScheduleSpecial"/>'s own
/// class remarks give the full "F91 mirrored, one substitution" rationale) plus no <c>Id</c> field at
/// all: unlike <see cref="ScheduleSegmentDto"/>'s PUT (which silently ignores a submitted id — a
/// whole-week replace), this endpoint only ever CREATES, so there is no id to ignore in the first
/// place.
///
/// <see cref="OnDate"/> serializes as a plain ISO date string (<c>"yyyy-MM-dd"</c>, System.Text.Json's
/// built-in <see cref="DateOnly"/> converter) — never a full timestamp; a special names a calendar
/// date, not an instant. <see cref="SpecialsController.Create"/> runs every SPEC F120.1 app-side gate
/// (30-minute step/range, end&gt;start, not-in-the-past, persona/show existence) against this body
/// BEFORE it ever becomes a <see cref="GenWave.Core.Domain.ScheduleSpecial"/> draft — see that
/// action's own remarks for the full order and each gate's 400 wording.
/// </summary>
public sealed record SpecialRequestDto(
    DateOnly OnDate,
    int StartMinute,
    int EndMinute,
    long? PersonaId,
    IReadOnlyList<string>? Genres,
    double? EnergyMin,
    double? EnergyMax,
    long? ShowId);
