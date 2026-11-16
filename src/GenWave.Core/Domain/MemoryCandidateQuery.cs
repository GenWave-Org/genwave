namespace GenWave.Core.Domain;

/// <summary>
/// One block's recall query against <see cref="Abstractions.IMemoryRecallStore"/> (SPEC F218.22).
/// Rows that are forgotten, or drafted and not yet approved, never qualify.
/// </summary>
/// <param name="Kind">A <see cref="MemoryKinds"/> value: lore, episode or life.</param>
/// <param name="Take">Most rows to return.</param>
/// <param name="NotAiredWithin">A row aired more recently than this window ago is skipped.</param>
/// <param name="Order">How eligible rows are ranked before <paramref name="Take"/> applies.</param>
public sealed record MemoryCandidateQuery(string Kind, int Take, TimeSpan NotAiredWithin, MemoryRecallOrder Order);
