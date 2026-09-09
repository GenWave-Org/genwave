namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The exact reference counts <see cref="SponsorRepository.DeleteIfUnreferencedAsync"/> reads before
/// attempting a delete (SPEC F171, F172; STORY-410; PLAN T432) — one row, always: three scalar
/// subqueries, never a GROUP BY that could come back short a row for a table with zero matches.
/// </summary>
sealed class SponsorReferenceCountsRow
{
    public int Briefs { get; set; }
    public int Spots { get; set; }
    public int Shows { get; set; }
}
