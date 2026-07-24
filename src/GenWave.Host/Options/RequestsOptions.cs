using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Env/compose-only throttle configuration for listener requests (SPEC F87, STORY-224, PLAN T86).
/// Bound from the <c>Requests</c> section, validated at startup (top-level properties, unlike
/// <see cref="StationOptions"/>'s nested knobs — <c>ValidateDataAnnotations()</c> DOES recurse into
/// a directly-bound options class's own properties). Deliberately absent from
/// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>: these are deployment-tuning
/// knobs an operator sets once, not live-editable — the three settings that ARE live
/// (<c>Enabled</c>/<c>OverrideEnvelope</c>/<c>WindowMinutes</c>) live on
/// <see cref="StationOptions.Requests"/> instead (<c>Station:Requests:*</c>).
/// </summary>
public sealed class RequestsOptions
{
    public const string Section = "Requests";

    /// <summary>Maximum accepted wish length in characters (SPEC F87.1). Longer ⇒ 400, nothing written.</summary>
    [Range(1, int.MaxValue)]
    public int WishMaxLength { get; set; } = 140;

    /// <summary>Minimum minutes between two accepted requests from the same IP (SPEC F87.3). 0 disables the cooldown.</summary>
    [Range(0, int.MaxValue)]
    public int PerIpCooldownMinutes { get; set; } = 5;

    /// <summary>Maximum accepted requests per IP per rolling day (SPEC F87.3); over cap ⇒ 429.</summary>
    [Range(1, int.MaxValue)]
    public int PerIpDailyCap { get; set; } = 20;

    /// <summary>Station-wide cap on pending rows (SPEC F87.3); at cap, the oldest pending row is evicted to make room.</summary>
    [Range(1, int.MaxValue)]
    public int PendingCap { get; set; } = 30;

    /// <summary>Hours a wish's raw text survives before the insert-time sweep nulls it (SPEC F87.8); parsed predicates and outcome are never swept.</summary>
    [Range(1, int.MaxValue)]
    public int WishRetentionHours { get; set; } = 24;
}
