namespace GenWave.Ads;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The ad render marker (SPEC F219.21): the value stamped into <c>ad_spot.render_version</c> when a spot's audio
/// lands, and compared by the stale re-render pass. It folds <see cref="AdRenderVersion.Current"/> (the pipeline)
/// with the engine and voices a render uses, so a file made on another engine or voice reads as stale and
/// re-renders in the background, one per idle tick, through the existing path. Computed, not configured: the
/// column stays an int (no schema change), holding a 31-bit digest kept above the legacy small version numbers
/// (0 = never stamped, 1 = pre-F219) so those rows are stale once.
/// </summary>
public static class AdRenderMarker
{
    // Why not AdDeterministicSeed.FromTerms: that seed is a signed 32-bit int (negative half the time, and any
    // small value possible), but this marker is compared against the legacy 0/1 version numbers already stored
    // in render_version and must never collide with them. So the digest is masked to 31 bits and floored
    // above the legacy range. FromTerms is also internal to its own picker contract; widening it would couple
    // the two.
    const int LegacyVersionCeiling = 1 << 16;

    /// <summary>
    /// Term order: pipeline version, engine name, station voice, announcer voice, cast voices. The voices are the
    /// per-engine maps as configured (SPEC F219.8), so editing the voice for the engine in use re-stamps; the
    /// engine is the stable name, never the endpoint.
    /// </summary>
    public static int For(AdLiveSettings live)
    {
        var canonical = AdDeterministicSeed.Canonical(
            AdRenderVersion.Current.ToString(System.Globalization.CultureInfo.InvariantCulture),
            live.ActiveEngine,
            live.StationVoice,
            live.AnnouncerVoice,
            string.Join('\n', live.CastVoices));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var value = BinaryPrimitives.ReadInt32BigEndian(digest) & int.MaxValue;
        return value < LegacyVersionCeiling ? value + LegacyVersionCeiling : value;
    }
}
