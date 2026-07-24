using System.Globalization;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;

namespace GenWave.Host.Engine;

/// <summary>
/// Resolves the <c>url=</c> annotation value carried on every feeder push (SPEC F88.4–F88.5,
/// STORY-223, PLAN T85): a music item's own per-track artwork-token URL, or the reserved
/// station-icon URL for a <c>tts:*</c> segment. Shared by <see cref="LiquidsoapControl.PushAsync"/>
/// and the safe-track endpoint (<c>InternalEndpoints</c>) so both resolve the same way, mirroring
/// how <see cref="LiquidsoapAnnotationBuilder"/> itself is shared between the two.
/// <para>
/// Returns <see langword="null"/> — "omit <c>url=</c> entirely" — whenever
/// <see cref="StationOptions.PublicBaseUrl"/> is blank, which is the whole of the F88.5 contract:
/// an empty base means no push, music or TTS, ever carries the key. <see cref="StationOptions"/>
/// is read live via <see cref="IOptionsMonitor{T}"/> on every call — never cached — so a live
/// <c>Station:PublicBaseUrl</c> edit reaches the very next push with no api restart, the same
/// shape every other Live station setting uses.
/// </para>
/// </summary>
public sealed class ArtworkUrlResolver(
    IOptionsMonitor<StationOptions> stationOptions,
    IArtworkTokenStore tokenStore)
{
    /// <summary>Convention shared with <see cref="LiquidsoapAnnotationBuilder"/>: TTS segment ids
    /// start with this, music ids never do.</summary>
    const string TtsIdPrefix = "tts:";

    /// <summary>
    /// The reserved artwork-token path segment every TTS push carries (SPEC F88.3's own no-oracle
    /// fallback mechanism — deliberately NOT a dedicated route). "station" is 7 characters, so
    /// <see cref="IArtworkTokenStore.ResolveAsync"/>'s "must be exactly 32 lowercase hex
    /// characters" guard rejects it before any database round trip, and
    /// <c>SpectatorArtworkController</c> falls straight through to its <c>ServeStationIcon</c>
    /// branch — the exact station-icon bytes every other no-oracle fallback path already serves.
    /// </summary>
    internal const string StationIconToken = "station";

    const string ArtworkPathPrefix = "/spectator/api/artwork/";

    /// <summary>
    /// Resolves <paramref name="item"/>'s artwork URL, or <see langword="null"/> when
    /// <see cref="StationOptions.PublicBaseUrl"/> is empty or <paramref name="item"/>'s id is
    /// neither a recognized TTS segment nor a parseable numeric music id (defensive — no shape of
    /// <see cref="MediaItem.MediaId"/> production ever produces today makes this branch reachable,
    /// but a caller must never fabricate a broken url= over a shape it does not recognize).
    /// </summary>
    public async Task<string?> ResolveAsync(MediaItem item, CancellationToken ct)
    {
        var baseUrl = stationOptions.CurrentValue.PublicBaseUrl;
        if (string.IsNullOrEmpty(baseUrl)) return null;

        // Trim a trailing '/' an operator may have typed (e.g. "https://example.test/") — without
        // this, ArtworkPathPrefix's own leading '/' would compose a "//spectator/..." double slash.
        baseUrl = baseUrl.TrimEnd('/');

        if (item.MediaId.StartsWith(TtsIdPrefix, StringComparison.Ordinal))
            return baseUrl + ArtworkPathPrefix + StationIconToken;

        if (!long.TryParse(item.MediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mediaId))
            return null;

        var token = await tokenStore.GetOrCreateTokenAsync(mediaId, ct);
        return baseUrl + ArtworkPathPrefix + token;
    }
}
