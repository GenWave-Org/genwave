namespace GenWave.MediaLibrary.Station;

/// <summary>
/// One (sponsor, state) count pair from <see cref="SponsorRepository.ListAsync"/>'s second round trip
/// (SPEC F172; STORY-407; PLAN T432) — <see cref="State"/> stays raw text (the <c>AdSpotRow.State</c>
/// precedent: <see cref="SponsorRepository"/> owns the <c>AdStateTokens</c> parse), grouped client-side
/// into each row's own <c>SponsorListRow.SpotsByState</c> dictionary.
/// </summary>
sealed class SponsorSpotStateCountRow
{
    public long SponsorId { get; set; }
    public string State { get; set; } = "";
    public int Count { get; set; }
}
