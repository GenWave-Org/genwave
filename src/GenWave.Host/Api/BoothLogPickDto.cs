using System.Text.Json.Serialization;

namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for <see cref="BoothLogEntryDto.Pick"/> (SPEC F86.2, STORY-217, PLAN T74): the exact
/// fields <see cref="GenWave.Core.Domain.BoothLogPickStamp"/> persists — fired-rule summaries and the
/// exploration flag, nothing else (scores, pool size, and the degradation step stay unexposed, F86.1)
/// — mirrored here as the Api layer's own wire type rather than serializing the domain record
/// directly, the same DTO/domain split every other type in this folder keeps.
///
/// <see cref="Nudge"/> (SPEC F151.4, STORY-371, PLAN T370) mirrors
/// <see cref="GenWave.Core.Domain.BoothLogPickStamp.Nudge"/> verbatim — already threshold-gated at the
/// write site (<c>BoothLogWriter</c>), so this DTO never re-applies the <c>|nudge| &gt;= 0.2</c> chip
/// gate itself. <c>JsonIgnore(WhenWritingNull)</c> makes it ABSENT from the JSON for every row below
/// the threshold or predating the column, the same discipline <see cref="BoothLogEntryDto.Pick"/>
/// already established for this optional field.
/// </summary>
public sealed record BoothLogPickDto(
    IReadOnlyList<BoothLogFiredRuleDto> FiredRules,
    bool IsExploration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Nudge = null);
