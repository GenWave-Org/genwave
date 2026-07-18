using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace GenWave.Host.Api;

/// <summary>
/// The single registration point for every named rate-limiter policy (SPEC F61.5) — the seam a
/// future policy (e.g. the spectator-surface limiter, STORY-166+/T13) extends by adding a policy
/// here, not by scattering <c>[EnableRateLimiting]</c> shapes and ad-hoc partition setup across
/// controllers.
///
/// <list type="bullet">
///   <item><see cref="Login"/> guards <see cref="AuthController.Login"/> against brute-force
///   password guessing (SPEC F61.5, STORY-165): a fixed 5-attempts-per-minute window, partitioned
///   by source IP so one caller's burst doesn't throttle another's. <see cref="HttpContext"/>
///   never has a null <see cref="System.Net.IPAddress"/> in production (Kestrel always populates
///   <c>Connection.RemoteIpAddress</c>), but the in-memory <c>TestServer</c> used by the host's
///   own spec suite does — those requests share one fallback partition, so the limiter still
///   throttles them (intentional: the spec relies on it).</item>
///   <item><see cref="Spectator"/> guards the whole public <see cref="SpectatorController"/>
///   (SPEC F62.11, STORY-171): a fixed 120-requests-per-minute window per source IP, generous
///   enough for a spectator page's own polling cadence (F62.10's 5/30/300s TTLs) but tight enough
///   to stop a single caller from hammering the playout host. The same source-IP partitioning
///   (and no-remote-ip fallback) idiom as <see cref="Login"/> — cached <c>OutputCache</c> hits
///   still count against this budget (SPEC F62.3): the limiter runs upstream of the cache in the
///   pipeline (see Program.cs), so it sees every request regardless of whether the cache would
///   have absorbed it. Simpler than teaching the limiter about cache hits, and still correct: a
///   caller that floods a cached route is still a caller worth throttling.</item>
/// </list>
/// </summary>
static class RateLimiterPolicies
{
    public const string Login = "login";
    public const string Spectator = "spectator";

    // Requests with no resolvable remote IP (TestServer) collapse into a single shared partition
    // rather than bypassing the limiter — a missing IP must still be throttleable, not exempt.
    const string NoRemoteIpPartitionKey = "no-remote-ip";

    /// <summary>
    /// Registers the named rate-limiter policies and the shared 429 rejection status. Call once
    /// from <see cref="AdminApiServiceCollectionExtensions"/>.
    /// </summary>
    public static IServiceCollection AddGenWaveRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // The framework default rejection status is 503 (service unavailable) — SPEC F61.5
            // requires callers see 429 (too many requests) instead.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(Login, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? NoRemoteIpPartitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));

            options.AddPolicy(Spectator, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? NoRemoteIpPartitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));
        });

        return services;
    }
}
