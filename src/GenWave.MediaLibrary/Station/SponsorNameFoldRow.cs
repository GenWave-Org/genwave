namespace GenWave.MediaLibrary.Station;

/// <summary>
/// One input name paired with its own <c>station.sponsor_fold</c> value, read back from
/// <see cref="SponsorRepository.UpsertPackSponsorsAsync"/>'s own <c>unnest</c> round trip (round-3
/// finding R1) — lets the caller detect two DIFFERENT input names folding to the SAME key and refuse
/// the whole batch before any row is written, without a second query per name.
/// </summary>
sealed class SponsorNameFoldRow
{
    public string Name { get; set; } = "";
    public string Folded { get; set; } = "";
}
