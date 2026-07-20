using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Retention configuration for <c>station.booth_log</c> (SPEC F72.3, STORY-195): rows older than
/// <see cref="RetentionDays"/> are deleted at insert time (<see cref="BoothLogRepository.AppendAsync"/>),
/// not by a separate job — see that method's own remarks. Config section "BoothLog".
/// </summary>
public sealed class BoothLogOptions
{
    public const string Section = "BoothLog";

    /// <summary>Rows older than this many days are evicted on every insert. Default 14 (SPEC F72.3).</summary>
    [Range(1, int.MaxValue)]
    public int RetentionDays { get; set; } = 14;
}
