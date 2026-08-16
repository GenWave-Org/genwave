namespace GenWave.Host.Auth;

/// <summary>Response item for <c>GET /api/stations</c>.</summary>
/// <param name="Id">The single station's id.</param>
/// <param name="Name">The live-effective station name (SPEC F44.6).</param>
/// <param name="StationImageToken">The current station image's opaque token (SPEC F131.3, PLAN T307
/// fix round), or <see langword="null"/> when the owner has never customized one — the authed admin
/// shell's own <c>generateMetadata</c> reads this field to compose its token-versioned tab-icon href
/// (<c>/api/station/image?v={token}</c>) WITHOUT a per-navigation fetch of the image's own bytes; a
/// bytes-free <c>IStationImageStore.GetTokenAsync</c> read, cheap enough to sit on this
/// already-per-navigation snapshot rather than earning a route of its own.</param>
public sealed record StationDto(long Id, string Name, string? StationImageToken);
