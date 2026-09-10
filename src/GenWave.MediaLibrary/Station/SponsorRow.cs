namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's flat projection of one <c>station.sponsor</c> row PLUS its referencing-brief and
/// referencing-show counts (SPEC F171.1, F172; STORY-406, STORY-407; PLAN T432) — used only by
/// <see cref="SponsorRepository.ListAsync"/>; every single-row read (<c>GetAsync</c>,
/// <c>CreateOwnerAsync</c>, <c>UpdateAsync</c>, <c>SetPausedAsync</c>, <c>UpsertPackSponsorAsync</c>)
/// binds straight to <c>GenWave.Core.Domain.Sponsor</c> instead (the <c>AdBriefRepository</c>
/// precedent: no raw-text/enum split, so the Core record IS Dapper's own projection target when no
/// extra join column is in play). <see cref="Version"/> is the row's <c>xmin</c> system column, cast
/// to text (the <c>AdSpotRow</c> precedent).
/// </summary>
sealed class SponsorRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? PackSlug { get; set; }
    public string? Tagline { get; set; }
    public string? About { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    public string? Tone { get; set; }
    public bool Paused { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Version { get; set; } = "";
    public int Briefs { get; set; }
    public int Shows { get; set; }
}
