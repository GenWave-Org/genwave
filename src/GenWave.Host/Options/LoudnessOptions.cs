using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Level-matching tunables (config section "Loudness"). Defaults follow PRD §10; streaming platforms
/// cluster around −14 to −16 LUFS.
/// </summary>
public sealed class LoudnessOptions
{
    public const string Section = "Loudness";

    /// <summary>
    /// Integrated loudness target in LUFS. Sane broadcast range is −40 to 0; positive values
    /// are physically nonsensical for a loudness target and are rejected.
    /// Default: −16 LUFS (streaming-platform sweet spot).
    /// </summary>
    [Range(-40.0, 0.0, ErrorMessage = "TargetLufs must be in [-40, 0].")]
    public double TargetLufs { get; set; } = -16.0;

    /// <summary>
    /// True-peak ceiling in dBTP. Must be ≤ 0 (a positive ceiling is above digital full scale).
    /// Default: −1 dBTP (standard inter-sample peak margin).
    /// </summary>
    [Range(-12.0, 0.0, ErrorMessage = "CeilingDbtp must be in [-12, 0].")]
    public double CeilingDbtp { get; set; } = -1.0;
}
