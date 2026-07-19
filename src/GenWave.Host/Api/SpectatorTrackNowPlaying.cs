namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/now-playing</c> when a real track is on-air (SPEC
/// F62.4). Deliberately a distinct type from <see cref="SpectatorPatterNowPlaying"/> — rather than
/// one shape with nullable title/artist — so a patter airing can omit the properties entirely
/// instead of merely nulling them (F62.9 disclosure-by-construction). Excludes media id, file
/// path, gain/loudness and every admin-only field by simply not having them.
/// </summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Track artist.</param>
/// <param name="StartedAt">UTC wall-clock instant the track started, for elapsed-time computation.</param>
/// <param name="DurationMs">
/// Track duration, if known (SPEC F50.3/F66.2). Null until the Host's duration rehydrator recovers
/// it from the catalog — never fabricated.
/// </param>
/// <param name="Listeners">
/// Live listener count (SPEC F62.12 addendum, STORY-179, gitea-#10), read from
/// <see cref="GenWave.Core.Abstractions.IListenerStatsSource"/>. Null when Icecast's admin stats
/// are unconfigured or unreachable — never fabricated, never surfaced as an error.
/// </param>
public sealed record SpectatorTrackNowPlaying(
    string? Title, string? Artist, DateTimeOffset StartedAt, int? DurationMs, int? Listeners)
{
    public string State => "onAir";
    public string Kind => "track";
}
