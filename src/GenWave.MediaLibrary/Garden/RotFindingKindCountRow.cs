namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// Flat Dapper projection of <see cref="RotFindingRepository.CountOpenByKindAsync"/>'s own grouped
/// <c>count(*)</c> query (SPEC F153.9, PLAN T372) — mirrors <see cref="RotFindingRow"/>'s own "one
/// settable-property class per query shape" convention.
/// </summary>
sealed class RotFindingKindCountRow
{
    public string Kind { get; set; } = "";
    public int Count { get; set; }
}
