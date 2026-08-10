namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/schedule/assign-show</c> request body (SPEC F119.2, STORY-313, PLAN T243): assigns
/// <see cref="ShowId"/> (or clears it, when null) onto the schedule block identified by
/// <see cref="BlockId"/> — by default across that block's whole contiguous same-persona run
/// (<see cref="ApplyToRun"/> <see langword="true"/> or absent, the grid side-panel's own default),
/// narrowed to just that one block when <see cref="ApplyToRun"/> is explicitly <see langword="false"/>
/// (the panel's own narrow-to-one checkbox). This is the ONLY wire surface with F119.2's run-span
/// semantics — <c>PUT /api/schedule</c>'s own <see cref="ScheduleSegmentDto.ShowId"/> also writes
/// <c>segment_schedule.show_id</c> now, but one row at a time, never fanned out across a run (see
/// <see cref="ScheduleController"/>'s class remarks).
///
/// <para>
/// <see cref="ApplyToRun"/> is wire-nullable (SPEC F6), NOT a plain <c>bool</c>: System.Text.Json
/// defaults a missing non-nullable <c>bool</c> property to <see langword="false"/>, which would silently
/// narrow an absent-field submission to the single clicked block — the OPPOSITE of the documented grid
/// default. <see cref="ScheduleController.AssignShow"/> resolves the absent case to
/// <see langword="true"/> server-side before it ever reaches
/// <see cref="GenWave.Core.Abstractions.IScheduleStore"/>.
/// </para>
/// </summary>
public sealed record AssignShowRequestDto(long BlockId, long? ShowId, bool? ApplyToRun);
