namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/now-playing</c> when TTS patter is on-air (SPEC F62.4).
/// Generated patter text and persona identity are operator content — this type simply has no
/// title/artist properties, so they cannot appear in the payload regardless of what the underlying
/// snapshot carries (F62.9 disclosure-by-construction). The page renders this as a "DJ break".
/// </summary>
/// <param name="StartedAt">UTC wall-clock instant the patter started, for elapsed-time computation.</param>
/// <param name="DurationMs">Measured patter duration (SPEC F66.1) — never fabricated.</param>
public sealed record SpectatorPatterNowPlaying(DateTimeOffset StartedAt, int? DurationMs)
{
    public string State => "onAir";
    public string Kind => "patter";
}
