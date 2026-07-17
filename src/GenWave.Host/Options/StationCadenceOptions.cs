using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Cadence toggles within the Station config section. Defaults mirror <see cref="GenWave.Core.Domain.CadenceConfig"/>.
/// </summary>
public sealed class StationCadenceOptions
{
    public bool LeadInBeforeEachTrack { get; set; } = true;
    public bool BackAnnounceAfterEachTrack { get; set; } = true;

    /// <summary>
    /// Insert a station ID jingle every N tracks. Must be non-negative; 0 disables station IDs
    /// entirely (SPEC F42.2, STORY-136) — the Orchestrator's <c>unitCount &gt; 0</c> guard means the
    /// first ID never airs before N units have elapsed, and never at all when this is 0.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "StationIdEveryNUnits must be at least 0 (0 disables station IDs).")]
    public int StationIdEveryNUnits { get; set; } = 4;
}
