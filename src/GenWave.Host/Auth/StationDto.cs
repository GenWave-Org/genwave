namespace GenWave.Host.Auth;

/// <summary>Response item for <c>GET /api/stations</c>.</summary>
public sealed record StationDto(long Id, string Name);
