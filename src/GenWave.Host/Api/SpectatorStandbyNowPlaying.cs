namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/now-playing</c> when there is nothing to disclose (SPEC
/// F62.4/F62.5): the feeder is still warming up (cold-start, no snapshot yet) or the safe-rotation
/// drain token is on-air. Both collapse to this single shape — the public surface never sees a 503
/// and never sees the word "drain" (F62.5).
/// </summary>
/// <param name="Listeners">
/// Live listener count (SPEC F62.12 addendum, STORY-179, gitea-#10), read from
/// <see cref="GenWave.Core.Abstractions.IListenerStatsSource"/>. Null when Icecast's admin stats
/// are unconfigured or unreachable — never fabricated, never surfaced as an error.
/// </param>
public sealed record SpectatorStandbyNowPlaying(int? Listeners)
{
    public string State => "standby";
}
