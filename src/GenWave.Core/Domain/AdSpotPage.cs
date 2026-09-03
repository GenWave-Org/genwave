namespace GenWave.Core.Domain;

/// <summary>
/// One page of <c>Abstractions.IAdSpotStore.ListByStateAsync</c>'s own state-scoped read (SPEC
/// F159.1/.2; STORY-389, STORY-392; PLAN T398) — the T385 kind-scoped paging precedent
/// (<see cref="RotFindingPage"/>) applied one seam over: <see cref="Total"/> is the EXACT count of
/// rows matching the same state filter as <see cref="Items"/>, computed in the same round trip
/// (never a <c>count(*) over()</c> window — an <c>offset</c> past the last row must still carry the
/// true total over an empty page).
/// </summary>
/// <param name="Items">This page's own rows, <c>state_changed_at desc, id desc</c>.</param>
/// <param name="Total">The exact count of rows matching the same state filter — never derived from
/// <see cref="Items"/>'s own count, which can be smaller than the page size on the last page.</param>
public sealed record AdSpotPage(IReadOnlyList<AdSpot> Items, int Total);
