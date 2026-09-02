namespace GenWave.Host.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The ONE ad-cadence knob within the Station config section this project itself binds (SPEC
/// F158.3, STORY-388, PLAN T397): <see cref="EveryNUnits"/>. Bound to <c>Station:Ads</c> — the SAME
/// raw config namespace <c>GenWave.Ads.AdSpotAntiRepeatOptions</c> independently binds
/// <c>AntiRepeatWindow</c> from (a DIFFERENT options class, a DIFFERENT project — that class's own
/// remarks document why a duplicate-namespace read, not a shared type, is the deliberate posture
/// here: <c>GenWave.Ads</c> must not reference <c>GenWave.Host</c>, and this project's own
/// <c>StationOptions</c> tree has no reason to reach into a feature package's options class either).
/// <c>Station:Ads:TargetCount</c>/<c>RefreshDays</c>/<c>AutoApprove</c> (SPEC F163.1's other three
/// allowlisted keys) have no bound options class anywhere yet — the same "allowlisted + validated,
/// no consumer wired" shape <c>Station:Audience</c> shipped under at PLAN T111 — future tasks
/// (T400-T402) are their first readers.
/// </summary>
public sealed class StationAdsOptions
{
    /// <summary>
    /// SPEC F158.3: an ad spot airs every N units. Must be non-negative; 0 disables the ad cadence
    /// entirely — mirrors <see cref="StationCadenceOptions.StationIdEveryNUnits"/>'s own "0 disables"
    /// contract exactly (the trigger this knob drives is that one's own twin,
    /// <c>GenWave.Orchestration.Orchestrator</c>'s <c>unitCount &gt; 0</c> guard included).
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "EveryNUnits must be at least 0 (0 disables ad spots).")]
    public int EveryNUnits { get; set; }
}
