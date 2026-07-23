using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The <c>library.media.artwork_token</c> seam (SPEC F88.2, gh-#105, STORY-222): lazy generation
/// of the opaque, non-enumerable token an artwork-aware player's URL carries, and the
/// token→media resolution the extraction seam (F88.3, a later task) needs to find the file.
///
/// <para>
/// Tokens exist so the spectator-facing artwork URL never has to carry a numeric media id —
/// media ids appear on no public surface (F62.9), and a random 128-bit value gives a would-be
/// prober nothing to enumerate: guessing tokens is computationally infeasible, and an unknown
/// token is indistinguishable (to the caller) from one that simply hasn't been minted yet.
/// </para>
///
/// <para>
/// Only tracks that actually air ever have their token emitted (F88.2) — this seam mints a
/// token lazily, on first need, rather than backfilling the whole catalog; a track that never
/// airs never gets one.
/// </para>
/// </summary>
public interface IArtworkTokenStore
{
    /// <summary>
    /// Returns the row's stored token, minting one (a random 128-bit value, rendered as 32
    /// lowercase hex chars) on the row's first need and persisting it. Concurrent first-asks for
    /// the same row never produce two different persisted tokens — see the implementation's
    /// remarks for the race-safety argument. Stable thereafter: once minted, a token never
    /// changes for the life of the row.
    /// </summary>
    Task<string> GetOrCreateTokenAsync(long mediaId, CancellationToken ct);

    /// <summary>
    /// Resolves a token back to the media row it names, or <see langword="null"/> if the token is
    /// unknown. A token that is not exactly 32 lowercase hex characters is rejected as unknown
    /// WITHOUT touching the database — malformed input can never justify a lookup, and this keeps
    /// the seam from ever becoming a shape-probing oracle (F88.2/F62.9).
    /// </summary>
    Task<ArtworkTokenResolution?> ResolveAsync(string token, CancellationToken ct);
}
