namespace GenWave.Host.Api;

/// <summary>
/// One <c>station.persona_taste</c> row surfaced by <c>GET /api/personas/{id}/taste</c> (SPEC F86.6,
/// STORY-219, PLAN T77). <see cref="PredicateSummary"/> is human-readable prose, not the raw
/// predicate shape (mirrors <see cref="BoothLogFiredRuleDto.Summary"/>'s own reduction). The context
/// gate is surfaced AS-IS: <see cref="DaysOfWeek"/> empty and <see cref="StartHour"/>/
/// <see cref="EndHour"/> both null all mean "no gate", the exact same unbounded-field convention
/// <see cref="GenWave.Core.Domain.TasteContext"/> itself uses — there is no separate "gated: bool"
/// wrapper to drift out of sync with the three fields it would summarize. <see cref="Weight"/> keeps
/// its sign (dislikes are taste too, SPEC F82.1).
/// </summary>
public sealed record PersonaTasteRuleDto(
    string PredicateSummary,
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    int? StartHour,
    int? EndHour,
    double Weight,
    DateTime UpdatedAt);
