using System.Globalization;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Artwork;
using GenWave.Host.Options;

namespace GenWave.Host.Engine;

/// <summary>
/// Resolves the <c>url=</c> annotation value carried on every feeder push (SPEC F88.4–F88.5,
/// STORY-223, PLAN T85; amended by SPEC F129.4, STORY-336, PLAN T300): a music item's own
/// per-track artwork-token URL, or — for a <c>tts:*</c> segment — either the DJ's own worn-face
/// token URL (a single-voice, persona-attributed item whose persona wears a face) or the reserved
/// station-icon URL (crosstalk, idents, and every other station-voiced kind — see
/// <see cref="ResolveDjTokenAsync"/> for the full F129.4 mapping). Shared by
/// <see cref="LiquidsoapControl.PushAsync"/> and the safe-track endpoint (<c>InternalEndpoints</c>)
/// so both resolve the same way, mirroring how <see cref="LiquidsoapAnnotationBuilder"/> itself is
/// shared between the two.
/// <para>
/// Returns <see langword="null"/> — "omit <c>url=</c> entirely" — whenever
/// <see cref="StationOptions.PublicBaseUrl"/> is blank, which is the whole of the F88.5 contract:
/// an empty base means no push, music or TTS, ever carries the key. <see cref="StationOptions"/>
/// is read live via <see cref="IOptionsMonitor{T}"/> on every call — never cached — so a live
/// <c>Station:PublicBaseUrl</c> edit reaches the very next push with no api restart, the same
/// shape every other Live station setting uses.
/// </para>
/// <para>
/// Also the real implementation of <see cref="IArtworkUrlEchoValidator"/> (PLAN T125 review F2):
/// <see cref="IsTrusted"/> answers the OPPOSITE direction's question — whether a url the ENGINE
/// echoed back on an engine-initiated play's output metadata is one THIS station actually stamped,
/// versus a hostile file tag's own <c>url</c>-shaped field. Both directions share the exact same
/// <see cref="StationOptions.PublicBaseUrl"/> + <see cref="ArtworkPathPrefix"/> composition, so
/// keeping them on one type is what keeps that composition a single source of truth.
/// </para>
/// </summary>
public sealed class ArtworkUrlResolver(
    IOptionsMonitor<StationOptions> stationOptions,
    IArtworkTokenStore tokenStore,
    IActivePersonaAccessor personaAccessor,
    PersonaAvatarTokenCache avatarTokenCache) : IArtworkUrlEchoValidator
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
            return await ResolveDjTokenAsync(item, ct) is { } djToken
                ? baseUrl + DjArtworkPaths.PathPrefix + djToken
                : baseUrl + ArtworkPathPrefix + StationIconToken;

        if (!long.TryParse(item.MediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mediaId))
            return null;

        var token = await tokenStore.GetOrCreateTokenAsync(mediaId, ct);
        return baseUrl + ArtworkPathPrefix + token;
    }

    /// <summary>
    /// SPEC F129.4 (amends F88.4) — resolves the worn-face token for a single-voice,
    /// persona-attributed TTS item, or <see langword="null"/> for every case that must stamp the
    /// station image instead: a station-voiced kind (<see cref="SegmentKind.StationId"/> — an
    /// ident/imaging piece always credits the station, gh-#96; <see cref="SegmentKind.Crosstalk"/>
    /// — "two voices = the station", ruled), an item with no <see cref="MediaItem.DjName"/>
    /// attribution at all (a music-only gap, or a pre-F129 caller), an unverifiable/disagreeing
    /// persona identity (see below), or a persona who simply wears no face.
    /// <para>
    /// <b>THE HONEST ATTRIBUTION SOURCE (PLAN T300 build note).</b> <see cref="MediaItem"/> carries
    /// no persona id of its own — only <see cref="MediaItem.DjName"/>, a display name
    /// (<c>SegmentRequest.PersonaName</c> verbatim, gh-#259) — and threading an id through the
    /// published <c>GenWave.Abstractions</c> contract is out of scope for this epic (every existing
    /// widening of <see cref="MediaItem"/>/<c>SegmentRequest</c> already had to become a defaulted
    /// body property, never a further positional parameter, for the identical binary-compat
    /// reason). Rather than build a SECOND cache to invert name→id, this resolves the id the SAME
    /// way <c>GenWave.Host.Artwork.DjIdentity.Agrees</c> already does for the
    /// now-playing payload: read <see cref="IActivePersonaAccessor.ActivePersonaId"/> (synchronous,
    /// zero I/O) as the candidate, then confirm
    /// <see cref="IActivePersonaAccessor.TryGetCachedName"/> for that id agrees with
    /// <paramref name="item"/>'s own <see cref="MediaItem.DjName"/> before trusting it — both
    /// members already ship on the published <see cref="IActivePersonaAccessor"/> seam, so this
    /// needs no Abstractions change at all.
    /// </para>
    /// <para>
    /// A push that races a boundary — the item was built for one persona but the accessor has
    /// already advanced to the next by the time this resolver runs — degrades to "no face" rather
    /// than risk pairing the WRONG face with this item: the identical "no face is safer than the
    /// wrong face" ruling <c>SpectatorController</c>'s own <c>RIGHT FACE OR NO FACE</c> remarks
    /// already document for the payload side, extended here to the stream. It self-heals the very
    /// next push once the accessor catches up.
    /// </para>
    /// </summary>
    async Task<string?> ResolveDjTokenAsync(MediaItem item, CancellationToken ct)
    {
        if (item.SegmentKind is null or SegmentKind.StationId or SegmentKind.Crosstalk) return null;
        if (item.DjName is not { } djName) return null;

        if (personaAccessor.ActivePersonaId is not { } activePersonaId) return null;
        // THE ONE IDENTITY GATE (PLAN T300 fix round F4) — shared with
        // Api.SpectatorController's own djAvatarUrl gate via DjIdentity.Agrees, so the payload and
        // the stream can never disagree on which face belongs to the on-air voice.
        if (!DjIdentity.Agrees(personaAccessor, activePersonaId, djName)) return null;

        return await avatarTokenCache.GetTokenAsync(activePersonaId, ct);
    }

    /// <inheritdoc/>
    public bool IsTrusted(string url)
    {
        var baseUrl = stationOptions.CurrentValue.PublicBaseUrl;
        if (string.IsNullOrEmpty(baseUrl)) return false;

        var trustedPrefix = baseUrl.TrimEnd('/') + ArtworkPathPrefix;
        return url.StartsWith(trustedPrefix, StringComparison.Ordinal);
    }
}
