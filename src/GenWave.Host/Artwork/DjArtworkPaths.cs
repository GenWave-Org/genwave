namespace GenWave.Host.Artwork;

/// <summary>
/// The ONE composer for the dj-avatar token route/URL shape (SPEC F129.1/F129.2/F129.4, PLAN T300
/// review rider). Before this task the shape lived as three independent literals that happened to
/// agree: <see cref="Api.SpectatorArtworkController.GetDjArtwork"/>'s own route attribute,
/// <see cref="Api.SpectatorController"/>'s own <c>DjAvatarPathPrefix</c> constant, and
/// <see cref="Engine.ArtworkUrlResolver"/>'s new need for the identical prefix on the feeder push
/// path — a drift between any two of them would have silently 404'd every worn face on whichever
/// side lagged. Every one of those three now reads THIS type instead.
/// </summary>
public static class DjArtworkPaths
{
    /// <summary>
    /// The route pattern segment <see cref="Api.SpectatorArtworkController.GetDjArtwork"/>'s
    /// <c>[HttpGet]</c>/<c>[HttpHead]</c> attributes carry, relative to that controller's own
    /// <c>[Route("spectator/api")]</c> class attribute — combines to the full
    /// <c>spectator/api/artwork/dj/{token}</c> route.
    /// </summary>
    public const string RouteSegment = "artwork/dj/{token}";

    /// <summary>
    /// The absolute-path prefix (no host) every URL composer stamps a token onto —
    /// <see cref="PathPrefix"/> + a token is exactly the path <see cref="RouteSegment"/> serves.
    /// Both <see cref="Api.SpectatorController"/>'s payload composition and
    /// <see cref="Engine.ArtworkUrlResolver"/>'s stream composition trim their own
    /// <c>Station:PublicBaseUrl</c> and append this plus the token.
    /// </summary>
    public const string PathPrefix = "/spectator/api/artwork/dj/";
}
