namespace GenWave.Host.Api;

/// <summary>
/// The 200 body for <c>GET /api/announcements/now-playing</c> (SPEC F145.3, F147.3's own sensor
/// consumer) — deliberately the smallest useful shape, not the rich admin/spectator projections
/// (<see cref="LiveController.GetNowPlaying"/>, <see cref="SpectatorController.GetNowPlaying"/>): a
/// home-automation sensor needs a track's identity, nothing else (no gain, no media id, no artwork,
/// no schedule lookahead). All three members are null together exactly when the station is in
/// standby (no snapshot yet, or the safe-rotation drain is on-air) — the same collapse
/// <see cref="SpectatorController"/>'s own standby shape performs, just without a separate
/// discriminant property: three nulls IS the standby state for this minimal a DTO.
/// </summary>
public sealed record AnnouncementNowPlayingDto(string? Title, string? Artist, string? DjName);
