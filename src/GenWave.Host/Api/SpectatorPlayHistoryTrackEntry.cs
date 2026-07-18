namespace GenWave.Host.Api;

/// <summary>
/// Public shape for a music entry in <c>GET /spectator/api/play-history</c> (SPEC F62.6). Mirrors
/// <see cref="SpectatorTrackNowPlaying"/>'s disclosure discipline: title/artist/airedAt only — no
/// media id, gain/loudness, or duration. <c>AiredAt</c> is the ring entry's <c>StartedAt</c>, the
/// instant this track began airing.
/// </summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Track artist.</param>
/// <param name="AiredAt">UTC wall-clock instant the track started airing.</param>
public sealed record SpectatorPlayHistoryTrackEntry(string? Title, string? Artist, DateTimeOffset AiredAt)
{
    public string Kind => "track";
}
