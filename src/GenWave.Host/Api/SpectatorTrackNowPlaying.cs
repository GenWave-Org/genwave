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
/// <param name="Dj">
/// The On-The-Air persona display name (SPEC F67.5-public, F93.1, STORY-244, PLAN T125), or null in
/// a music-only segment or grid gap — never the admin persona id, backstory, or any other field.
/// </param>
/// <param name="UpNext">
/// Exactly one upcoming segment (SPEC F93.2), or null when there is nothing to announce — see
/// <see cref="SpectatorUpNext"/>'s own remarks for the same-persona collapse rule.
/// </param>
/// <param name="ArtworkUrl">
/// The F88 token artwork URL for this track (SPEC F93.3, STORY-245, PLAN T125), or null when there
/// is no art or <c>Station:PublicBaseUrl</c> is unset — the page falls back to the station icon.
/// </param>
public sealed record SpectatorTrackNowPlaying(
    string? Title, string? Artist, DateTimeOffset StartedAt, int? DurationMs, int? Listeners,
    string? Dj, SpectatorUpNext? UpNext, string? ArtworkUrl)
{
    public string State => "onAir";
    public string Kind => "track";
}
