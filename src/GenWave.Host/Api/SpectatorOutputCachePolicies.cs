namespace GenWave.Host.Api;

/// <summary>
/// The single registration point for every named <c>OutputCache</c> policy on the public
/// spectator surface (SPEC F62.10, STORY-171/T13) — applied per-action via
/// <c>[OutputCache(PolicyName = ...)]</c> on <see cref="SpectatorController"/>. Absorbs repeat
/// requests within each route's TTL at the process's own in-memory cache, so a spike in
/// spectators never reaches the live playout state (<see cref="NowPlayingService"/>,
/// <see cref="PlayHistoryService"/>) or the media catalog.
/// <para>
/// The built-in output cache middleware does not emit a <c>Cache-Control</c> response header on
/// its own — that is <see cref="SpectatorCacheControlAttribute"/>'s job, applied alongside these
/// policies with the same TTL, so a fronting CDN/reverse-proxy can also absorb the same spike
/// without ever reaching this process (SPEC F62.11).
/// </para>
/// </summary>
static class SpectatorOutputCachePolicies
{
    /// <summary>SPEC F62.10: now-playing changes on every track/patter transition, so its TTL is
    /// the shortest of the four — 5 seconds.</summary>
    public const string NowPlaying = "spectator-now-playing";

    /// <summary>SPEC F62.10: play-history and stats change far less often than now-playing — 30
    /// seconds.</summary>
    public const string PlayHistory = "spectator-play-history";

    /// <summary>SPEC F62.10: play-history and stats change far less often than now-playing — 30
    /// seconds.</summary>
    public const string Stats = "spectator-stats";

    /// <summary>SPEC F62.10: about is effectively static between operator settings changes — 300
    /// seconds.</summary>
    public const string About = "spectator-about";

    /// <summary>
    /// Registers the named output-cache policies. Call once from <c>Program.cs</c> alongside
    /// <c>AddOutputCache</c>'s other consumers (there are none yet — the spectator surface is the
    /// first).
    /// </summary>
    public static IServiceCollection AddGenWaveSpectatorOutputCaching(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            // SetVaryByQuery(): none of these endpoints takes query parameters, and the default
            // vary-by-every-query-string would let junk ?x=N queries fan out the cache key —
            // each unique key a fresh backend hit, diluting the cache (up to the rate limit).
            // One key per route: query strings never reach the projection.
            options.AddPolicy(NowPlaying, builder => builder.Expire(TimeSpan.FromSeconds(5)).SetVaryByQuery([]));
            options.AddPolicy(PlayHistory, builder => builder.Expire(TimeSpan.FromSeconds(30)).SetVaryByQuery([]));
            options.AddPolicy(Stats, builder => builder.Expire(TimeSpan.FromSeconds(30)).SetVaryByQuery([]));
            options.AddPolicy(About, builder => builder.Expire(TimeSpan.FromSeconds(300)).SetVaryByQuery([]));
        });

        return services;
    }
}
