using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Rotation knobs within the Station config section (SPEC F41.6). Defaults mirror
/// <see cref="GenWave.Core.Domain.RotationSettings"/>. Bound to <c>Station:Rotation</c>;
/// live-editable via the F19 settings overlay (joins the allowlist in
/// <see cref="Configuration.StationSettingsAllowlist"/>).
/// </summary>
public sealed class StationRotationOptions
{
    /// <summary>How many recently-aired media ids the feeder remembers for repeat-avoidance. 0 disables anti-repeat.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "RecentWindow must be non-negative.")]
    public int RecentWindow { get; set; } = 20;

    /// <summary>No same artist within the last N music selections (preference tier). 0 disables.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "ArtistSeparation must be non-negative.")]
    public int ArtistSeparation { get; set; } = 2;
}
