namespace GenWave.Host.Api;

/// <summary>
/// Public shape for a TTS patter entry in <c>GET /spectator/api/play-history</c> (SPEC F62.6).
/// Mirrors <see cref="SpectatorPatterNowPlaying"/>'s anonymization: no title/artist properties at
/// all — generated patter text and persona identity are operator content — so an aired patter break
/// carries only <c>kind</c> and <c>airedAt</c> (F62.9 disclosure-by-construction).
/// </summary>
/// <param name="AiredAt">UTC wall-clock instant the patter started airing.</param>
public sealed record SpectatorPlayHistoryPatterEntry(DateTimeOffset AiredAt)
{
    public string Kind => "patter";
}
