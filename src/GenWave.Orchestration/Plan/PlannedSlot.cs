using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// One slot of a <see cref="BreakPlan"/>, in kick order (SPEC F187): what plays (<see cref="Kind"/>),
/// where its content comes from (<see cref="Source"/>), the claim the planner holds on it
/// (<see cref="Reservation"/>), how a failed delivery is reported (<see cref="Drop"/>), and whether
/// the render phase must honor the plan's render budget for it (<see cref="ObserveDuration"/>).
/// </summary>
/// <param name="Ordinal">This slot's 1-based position in kick order (SPEC F187.2).</param>
/// <param name="Kind">The broadcast role this slot plays.</param>
/// <param name="Source">Where the slot's content comes from.</param>
/// <param name="Reservation">
/// The claim this slot carries, or <see langword="null"/> for an unreserved slot — back-announce,
/// lead-in, a handoff piece, time/date, or context (SPEC F187.3).
/// </param>
/// <param name="Drop">How a failed delivery for this slot is reported.</param>
/// <param name="ObserveDuration">
/// <see langword="true"/> when the render phase must measure this slot against the plan's render
/// budget (SPEC F186.3's "exceeds the render budget"); <see langword="false"/> for a
/// <see cref="ReadySource"/> slot, which never renders at air time.
/// </param>
public sealed record PlannedSlot(
    int Ordinal,
    SegmentKind Kind,
    SlotSource Source,
    Reservation? Reservation,
    DropPolicy Drop,
    bool ObserveDuration);
