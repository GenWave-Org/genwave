namespace GenWave.Core.Domain;

/// <summary>
/// A <see cref="TasteRule"/>'s time gate (SPEC F82.1, F82.5): the rule only fires when the pick's
/// day falls in <see cref="DaysOfWeek"/> (empty = every day — no day gate) AND the pick's hour falls
/// in [<see cref="StartHour"/>, <see cref="EndHour"/>) (either side left <c>null</c> leaves that
/// bound open). The PRD's Sunday-Zeppelin acceptance demo (PRD-envelopes-selection.md issue C) is
/// exactly this shape: <c>DaysOfWeek: [Sunday], StartHour: 6, EndHour: 12</c>. Hour/day gate
/// evaluation itself is the ranker's concern (F82.5, a later task); this shape only carries the gate.
/// </summary>
public sealed record TasteContext(
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    int? StartHour,
    int? EndHour);
