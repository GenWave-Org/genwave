namespace GenWave.Host.Artwork;

/// <summary>
/// The ONE composer for the station-image token route/URL shape (SPEC F131.2, PLAN T300/T307 review
/// rider) — mirrors <see cref="DjArtworkPaths"/>'s own reasoning one-for-one, for the THIRD token
/// space this codebase's push path composes a URL for. Before this task the station's own "no
/// customization" fallback path lived as a private literal split across two constants inside
/// <see cref="Engine.ArtworkUrlResolver"/> (its own former <c>ArtworkPathPrefix</c> +
/// <c>StationIconToken</c>); post-T307 the station URL composes at 2+ sites
/// (<see cref="Engine.ArtworkUrlResolver.ResolveAsync"/>'s own push-path stamp and
/// <see cref="Api.SpectatorArtworkController.GetStationArtwork"/>'s own route attribute) — both now
/// read THIS type instead of either independently re-spelling the same path.
/// </summary>
public static class StationArtworkPaths
{
    /// <summary>
    /// The route pattern segment <see cref="Api.SpectatorArtworkController.GetStationArtwork"/>'s
    /// <c>[HttpGet]</c>/<c>[HttpHead]</c> attributes carry, relative to that controller's own
    /// <c>[Route("spectator/api")]</c> class attribute — combines to the full
    /// <c>spectator/api/artwork/station/{token}</c> route. Reached ONLY when the station image is
    /// customized (SPEC F131.2) — the CUSTOMIZED-case counterpart to
    /// <see cref="ShippedFallbackPath"/> below.
    /// </summary>
    public const string RouteSegment = "artwork/station/{token}";

    /// <summary>
    /// The absolute-path prefix (no host) every URL composer stamps a token onto for the CUSTOMIZED
    /// case — <see cref="PathPrefix"/> + a token is exactly the path <see cref="RouteSegment"/>
    /// serves. <see cref="Engine.ArtworkUrlResolver"/>'s own push-path composition trims its own
    /// <c>Station:PublicBaseUrl</c> and appends this plus the current station-image token, mirroring
    /// <see cref="DjArtworkPaths.PathPrefix"/>'s own idiom exactly.
    /// </summary>
    public const string PathPrefix = "/spectator/api/artwork/station/";

    /// <summary>
    /// The reserved, NO-TOKEN fallback path (SPEC F88.3's own no-oracle mechanism, folded in here at
    /// PLAN T307's review rider — formerly <see cref="Engine.ArtworkUrlResolver"/>'s own private
    /// <c>ArtworkPathPrefix + StationIconToken</c> composition) — the SHIPPED-CONSTANT URL every TTS
    /// push carries when the station image is NOT customized (byte-identical to pre-F131:
    /// <see cref="Api.SpectatorArtworkController.GetArtwork"/>'s own generic per-track route already
    /// resolves this exact path to the row-else-shipped-logo fallback, since "station" is 7
    /// characters and therefore fails <see cref="GenWave.Core.Domain.ArtworkToken.IsWellFormed"/>
    /// before any database round trip — no dedicated route was ever needed for THIS half).
    /// </summary>
    public const string ShippedFallbackPath = "/spectator/api/artwork/station";
}
