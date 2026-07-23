namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for <see cref="BoothLogEntryDto.Pick"/> (SPEC F86.2, STORY-217, PLAN T74): the exact
/// fields <see cref="GenWave.Core.Domain.BoothLogPickStamp"/> persists — fired-rule summaries and the
/// exploration flag, nothing else (scores, pool size, and the degradation step stay unexposed, F86.1)
/// — mirrored here as the Api layer's own wire type rather than serializing the domain record
/// directly, the same DTO/domain split every other type in this folder keeps.
/// </summary>
public sealed record BoothLogPickDto(IReadOnlyList<BoothLogFiredRuleDto> FiredRules, bool IsExploration);
