using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Show-domain knobs within the Station config section (SPEC F116.3, STORY-308, PLAN T249). Bound to
/// <c>Station:Shows</c>.
/// </summary>
public sealed class StationShowsOptions
{
    /// <summary>
    /// How often, in minutes, the show-flavor patter line may air per show (SPEC F116.3) — an
    /// ordinary LeadIn/BackAnnounce break may carry the on-air show's flavor as spoken color, sharing
    /// F107.5's single extra-line slot with the context-fact patter lane (context always wins when
    /// both are due; see <c>GenWave.Orchestration.ShowFlavorLineGate</c>). 0 (the default) disables
    /// it entirely — an opt-in feature, not a default-on one. Documentation-only <see cref="RangeAttribute"/>
    /// (<c>StationOptionsValidator</c> is the real boot floor — root <c>ValidateDataAnnotations()</c>
    /// does not recurse into nested option classes, the <see cref="StationCadenceOptions"/> precedent).
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "PatterCadenceMinutes must be at least 0 (0 disables the show-flavor line).")]
    public int PatterCadenceMinutes { get; set; }
}
