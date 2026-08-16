namespace GenWave.Host.Api;

/// <summary>
/// Response body for every <see cref="StationImageController"/> write route that leaves a station
/// image in place — <c>PUT</c> (SPEC F131.1, STORY-339, PLAN T307). Narrower than
/// <see cref="GenWave.Core.Domain.StationImage"/>: never carries <c>Bytes</c>/<c>ByteSize</c>/
/// <c>Sha256</c> — mirrors <see cref="PersonaAvatarDto"/>'s own remarks on why echoing the payload
/// back here would be dead weight no caller reads (the public, anonymous token route resolves the
/// bytes instead). Carries no <c>Source</c>/<c>ImportedFrom</c> either — unlike a persona's worn
/// face, the station image has no catalog-acquisition path (SPEC F131 is upload/delete only).
/// </summary>
/// <param name="Token">The 128-bit hex token this image now serves under — freshly ROTATED by this
/// write (SPEC F131.1): a caller holding the PREVIOUS token from before this call already knows it is
/// stale without needing to re-read anything.</param>
/// <param name="UpdatedAt">When this write was persisted.</param>
public sealed record StationImageDto(string Token, DateTime UpdatedAt);
