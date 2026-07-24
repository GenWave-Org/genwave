namespace GenWave.Host.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The three LIVE-editable listener-request knobs (SPEC F87.2, F87.6, STORY-224) within the Station
/// config section — the rest of the F87 throttle surface (<c>WishMaxLength</c>,
/// <c>PerIpCooldownMinutes</c>, <c>PerIpDailyCap</c>, <c>PendingCap</c>, <c>WishRetentionHours</c>)
/// binds from <see cref="RequestsOptions"/> instead (env/compose-only, not operator-editable).
/// Bound to <c>Station:Requests</c>; joins the allowlist in
/// <see cref="Configuration.StationSettingsAllowlist"/>.
/// </summary>
public sealed class StationRequestsOptions
{
    /// <summary>
    /// Kill switch (SPEC F87.2). Off by default — disabled means the endpoint 404s (F61 surface-off
    /// semantics), never a distinguishable "requests are closed" response.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When true (the default), a matched request bypasses envelope genre/energy and
    /// rotation-recency at fulfillment (SPEC F87.6) — <c>never_play</c>/<c>eligible=false</c> stay
    /// law either way. Default-true rationale (ARCHITECTURE.md): audibly honoring listeners IS the
    /// product; integrity-minded operators flip one switch.
    /// </summary>
    public bool OverrideEnvelope { get; set; } = true;

    /// <summary>
    /// Minutes after <c>received_at</c> an unfulfilled request stays live before expiring (SPEC
    /// F87.6). Documentation-only <c>[Range]</c> — <see cref="StationOptionsValidator"/> is the real
    /// boot floor, the same "nested class, root <c>ValidateDataAnnotations()</c> doesn't recurse"
    /// story as every other <c>Station:*</c> knob.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int WindowMinutes { get; set; } = 15;
}
