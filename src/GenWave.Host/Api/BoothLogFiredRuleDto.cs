namespace GenWave.Host.Api;

/// <summary>
/// One entry of <see cref="BoothLogPickDto.FiredRules"/> (SPEC F86.2, STORY-217, PLAN T74): a
/// human-readable summary of the taste rule that fired and its signed weight — mirrors
/// <see cref="GenWave.Core.Domain.BoothLogFiredRuleSummary"/>.
/// </summary>
public sealed record BoothLogFiredRuleDto(string Summary, double Weight);
