namespace GenWave.Host.Options;

/// <summary>
/// Configuration for trusting a fronting reverse proxy's <c>X-Forwarded-For</c> header (deferred
/// finding from T04's review, addressed alongside the spectator rate limiter in STORY-171/T13:
/// per-IP limiting is only meaningful if <c>RemoteIpAddress</c> reflects the real client, not the
/// proxy's own address). Bound from the <c>Proxy</c> config section — like
/// <see cref="AdminOptions.Enabled"/>, env/compose-only: flipping it requires a container
/// recreate, not a live setting.
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// CIDR ranges (e.g. <c>"172.20.0.0/16"</c>) of reverse proxies trusted to set
    /// <c>X-Forwarded-For</c>. Empty by default, which leaves the forwarded-headers middleware
    /// registered but effectively inert: its own default known-networks/known-proxies trust only
    /// loopback, and a compose-network proxy (e.g. Caddy, see PLAN T19's reference topology) is
    /// never loopback. Never widen this to trust an unlisted network — a spoofed
    /// <c>X-Forwarded-For</c> header from an untrusted source would let a client dodge the
    /// per-IP spectator rate limiter (<see cref="RateLimiterPolicies.Spectator"/>).
    /// </summary>
    public IReadOnlyList<string> TrustedNetworks { get; init; } = [];
}
