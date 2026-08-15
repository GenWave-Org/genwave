namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for <see cref="BoothLogEntryDto.Crosstalk"/> (SPEC F127.11, STORY-329, PLAN T287, review
/// finding F3) — the full two-voice script a <c>SegmentKind.Crosstalk</c> row's own <c>pick</c> jsonb
/// carries, narrowed the same way <see cref="BoothLogPickDto"/> narrows a persona pick's stamp one seam
/// over. Mutually exclusive with <see cref="BoothLogEntryDto.Pick"/> on any one row — a music pick and
/// a crosstalk exchange never coexist (<c>BoothLogWriter.BuildPickStamp</c>'s own remarks) — so a row
/// carries at most one of the two, never both.
/// </summary>
public sealed record BoothLogCrosstalkScriptDto(IReadOnlyList<BoothLogCrosstalkLineDto> Lines);
