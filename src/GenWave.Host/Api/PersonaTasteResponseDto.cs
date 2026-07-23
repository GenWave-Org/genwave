namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /api/personas/{id}/taste</c> response (SPEC F86.6, STORY-219, PLAN T77): the persona's
/// taste rules grouped by source, plus the accrued count against the cap
/// (<see cref="GenWave.Core.Abstractions.IPersonaTasteAccrualStore.Cap"/>, SPEC F84.3) so the Admin
/// UI (PLAN T78) can render the cap meter without hardcoding its own copy of that number.
/// <see cref="Authored"/>/<see cref="Operator"/> rows are exempt from the cap BY CONSTRUCTION
/// (F84.3) — only <see cref="Accrued"/>'s own row count feeds <see cref="AccruedCount"/>.
/// </summary>
public sealed record PersonaTasteResponseDto(
    IReadOnlyList<PersonaTasteRuleDto> Authored,
    IReadOnlyList<PersonaTasteRuleDto> Operator,
    IReadOnlyList<PersonaTasteRuleDto> Accrued,
    int AccruedCount,
    int AccruedCap);
