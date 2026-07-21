using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Boundary-aware selection bias knobs within the Station config section (SPEC F74.3,
/// STORY-198). Bound to <c>Station:BoundaryBias</c>. Boot/env-tunable only for v1 — deliberately
/// NOT joined to the <c>Station:*</c> live-edit allowlist (<see cref="Configuration.StationSettingsAllowlist"/>):
/// unlike Cadence/Rotation/RenderBudget this is a soft, subordinate tuning knob (SPEC F74.3 — "soft
/// bias, never a filter") with no operator urgency for a live <c>PUT /api/settings</c> path yet.
/// </summary>
public sealed class StationBoundaryBiasOptions
{
    /// <summary>
    /// Minutes ahead of a pending deferral's due time that track selection starts biasing toward a
    /// track whose end lands near that due time. Must be non-negative; 0 disables the bias
    /// entirely. Carries a DataAnnotations <c>[Range(0, int.MaxValue)]</c> attribute as
    /// documentation only — like <see cref="StationCadenceOptions.StationIdEveryNUnits"/>,
    /// <c>ValidateDataAnnotations()</c> on the root <c>StationOptions</c> does not recurse into
    /// nested option classes, so <see cref="StationOptionsValidator"/> is what actually enforces
    /// this floor at boot.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "LookaheadMinutes must be non-negative (0 disables the bias).")]
    public int LookaheadMinutes { get; set; } = 10;
}
