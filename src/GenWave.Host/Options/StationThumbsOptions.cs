namespace GenWave.Host.Options;

/// <summary>
/// The ONE Live-editable Library Gardener knob (SPEC F150.2, F155.1, STORY-380, STORY-369; PLAN
/// T357, T366) within the Station config section — every other Gardener knob
/// (<c>HalfLifeDays</c>/<c>Saturation</c>/<c>ThumbCooldownSeconds</c>/<c>ThumbDailyCap</c>/etc.)
/// binds from <see cref="GenWave.MediaLibrary.Options.GardenerOptions"/> instead (env/compose-only,
/// boot-validated, not operator-editable — that class's own remarks explain the split). Bound to
/// <c>Station:Thumbs</c>; joins the allowlist in <see cref="Configuration.StationSettingsAllowlist"/>
/// (the <c>Station:Thumbs:Enabled</c> row T357 already added there) — mirrors
/// <see cref="StationRequestsOptions"/>'s own one-field shape one seam over.
/// </summary>
public sealed class StationThumbsOptions
{
    /// <summary>
    /// Kill switch (SPEC F150.2). Off by default (Live default false, per-deployment demo default
    /// true) — disabled means <c>POST /spectator/api/thumbs</c> 404s (F61 surface-off semantics,
    /// the <see cref="StationRequestsOptions.Enabled"/> precedent) and the spectator page's thumbs
    /// controls are absent with the same silence, never a distinguishable "thumbs are closed"
    /// response.
    /// </summary>
    public bool Enabled { get; set; }
}
