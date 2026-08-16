namespace GenWave.Core.Domain;

/// <summary>
/// The one 32-lowercase-hex opaque-token shape every artwork/avatar surface shares (SPEC F88.2,
/// extended by F129.1 to the on-air persona's worn-face token) — <see cref="IsWellFormed"/> is the
/// single non-enumerability guard a caller runs before a store round trip: a malformed token can
/// never justify one. Homed here (no framework deps) rather than duplicated per project —
/// <c>ArtworkTokenRepository.ResolveAsync</c> (GenWave.MediaLibrary, per-track cover art) and
/// <c>SpectatorArtworkController.GetDjArtwork</c> (GenWave.Host, the worn face) both call this SAME
/// predicate rather than each carrying its own copy that could silently drift apart.
/// </summary>
public static class ArtworkToken
{
    /// <summary>32 hex chars = 16 bytes = 128 bits (SPEC F88.2).</summary>
    public const int Length = 32;

    /// <summary>True when <paramref name="token"/> is exactly <see cref="Length"/> lowercase hex
    /// characters — the one shape every real token this codebase mints ever has.</summary>
    public static bool IsWellFormed(string token)
    {
        if (token.Length != Length)
            return false;

        foreach (var c in token)
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;

        return true;
    }
}
