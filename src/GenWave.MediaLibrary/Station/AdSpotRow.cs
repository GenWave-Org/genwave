namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's flat projection of one <c>station.ad_spot</c> row (SPEC F159.1, F159.2; STORY-389; PLAN
/// T398) — mirrors <c>Garden.RotFindingRow</c>'s own "one settable-property class per query shape"
/// convention, one column pair over. <see cref="State"/>/<see cref="Source"/> stay raw text here
/// (bound via <c>::text</c> casts in every query — <see cref="AdSpotRepository"/> owns the
/// <see cref="GenWave.Core.Domain.AdStateTokens"/>/<see cref="GenWave.Core.Domain.AdSourceTokens"/>
/// parse), and <see cref="VoicePlan"/> stays raw <c>jsonb</c> text (opaque, the <c>RotFinding.Evidence</c>
/// precedent) — the SAME "convert explicitly only at the boundary" restraint
/// <c>Garden.RotFindingRow</c>'s own remarks document one seam over. <see cref="Version"/> is the
/// row's <c>xmin</c> system column, cast to text (the <c>MediaRow</c>/<c>AdminMediaDto</c> precedent)
/// so Dapper maps it as a plain string rather than needing a custom <c>xid</c> handler.
/// </summary>
sealed class AdSpotRow
{
    public long Id { get; set; }
    public string Brand { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Brief { get; set; }
    public string? Script { get; set; }
    public string Source { get; set; } = "";
    public string? PackSlug { get; set; }
    public int SpotSeconds { get; set; }
    public string? VoicePlan { get; set; }
    public long? BedMediaId { get; set; }
    public string State { get; set; } = "";
    public string? FailReason { get; set; }
    public long? MediaId { get; set; }
    public int Generation { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime StateChangedAt { get; set; }
    public DateTime? RenderedAt { get; set; }
    public DateTime? RetiredAt { get; set; }
    public string Version { get; set; } = "";
}
