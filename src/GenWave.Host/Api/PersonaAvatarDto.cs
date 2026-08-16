namespace GenWave.Host.Api;

/// <summary>
/// Response body for every <see cref="PersonaAvatarController"/> write route that leaves a face in
/// place — <c>PUT</c> and <c>POST .../from-pack</c> (SPEC F128.5/.6, STORY-333, PLAN T295). Narrower
/// than <see cref="GenWave.Core.Domain.PersonaAvatar"/>: never carries <c>Bytes</c>/<c>ByteSize</c>/
/// <c>Sha256</c> — this controller has no read route for the face's own bytes (a future consumer
/// resolves them through the public, anonymous token route, PLAN T298, exactly the way an admin-UI
/// thumbnail already resolves F88 artwork), so echoing the payload back here would be dead weight no
/// caller reads.
/// </summary>
/// <param name="PersonaId">The persona this face belongs to (the route id).</param>
/// <param name="Token">The 128-bit hex token this face now serves under — freshly ROTATED by this
/// write (SPEC F129.1): a caller holding the PREVIOUS token from before this call already knows it is
/// stale without needing to re-read anything.</param>
/// <param name="Source">Either <c>"upload"</c> or <c>"catalog"</c> — mirrors
/// <see cref="GenWave.Core.Domain.PersonaAvatarSource"/>'s own two values, serialized as the same
/// lowercase text the store's own <c>source</c> CHECK constraint uses (never the enum's raw ordinal).</param>
/// <param name="ImportedFrom">The pack slug this face was copied from, when <see cref="Source"/> is
/// <c>"catalog"</c>; <see langword="null"/> for an <c>"upload"</c> row.</param>
/// <param name="UpdatedAt">When this write was persisted.</param>
public sealed record PersonaAvatarDto(long PersonaId, string Token, string Source, string? ImportedFrom, DateTime UpdatedAt);
