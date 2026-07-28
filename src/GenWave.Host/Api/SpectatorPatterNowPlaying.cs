namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/now-playing</c> when TTS patter is on-air (SPEC F62.4).
/// Generated patter text and persona identity are operator content — this type simply has no
/// title/artist properties, so they cannot appear in the payload regardless of what the underlying
/// snapshot carries (F62.9 disclosure-by-construction). The page renders this as a "DJ break".
/// </summary>
/// <param name="StartedAt">UTC wall-clock instant the patter started, for elapsed-time computation.</param>
/// <param name="DurationMs">Measured patter duration (SPEC F66.1) — never fabricated.</param>
/// <param name="Listeners">
/// Live listener count (SPEC F62.12 addendum, STORY-179, gitea-#10), read from
/// <see cref="GenWave.Core.Abstractions.IListenerStatsSource"/>. Null when Icecast's admin stats
/// are unconfigured or unreachable — never fabricated, never surfaced as an error.
/// </param>
/// <param name="Dj">
/// The On-The-Air persona display name (SPEC F67.5-public, F93.1, STORY-244, PLAN T125) — the same
/// field <see cref="SpectatorTrackNowPlaying"/> carries, added here rather than reviving the F62.5
/// amendment's tentative <c>artist</c>-on-patter shape (that field never shipped): F93.1 gives both
/// on-air shapes one persona-name field with one name. Null in a music-only segment or grid gap —
/// never generated patter text or any other persona field (F62.9 still holds for those).
/// </param>
/// <param name="UpNext">
/// Exactly one upcoming segment (SPEC F93.2), or null when there is nothing to announce — see
/// <see cref="SpectatorUpNext"/>'s own remarks for the same-persona collapse rule.
/// </param>
public sealed record SpectatorPatterNowPlaying(
    DateTimeOffset StartedAt, int? DurationMs, int? Listeners, string? Dj, SpectatorUpNext? UpNext)
{
    public string State => "onAir";
    public string Kind => "patter";
}
