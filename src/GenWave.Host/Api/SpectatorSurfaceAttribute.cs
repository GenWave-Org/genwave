namespace GenWave.Host.Api;

/// <summary>
/// Endpoint metadata marking a route as part of the public spectator surface (SPEC F62,
/// F62.2's surface-gate mechanics). <see cref="SurfaceGateMiddleware"/> returns a bare 404 for
/// any endpoint carrying this marker when <c>Station:SpectatorMode</c> is false — the surface
/// does not exist for a deployment that has not opted into it.
///
/// No endpoint carries this marker yet — the spectator route group lands in a later task (T10).
/// This attribute exists now so <see cref="SurfaceGateMiddleware"/> has both branches in place.
///
/// Usable as a class/method attribute (<c>[SpectatorSurface]</c>) on MVC controllers/actions, and
/// as plain endpoint metadata via
/// <c>RouteGroupBuilder.WithMetadata(new SpectatorSurfaceAttribute())</c> for minimal-API route
/// groups.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class SpectatorSurfaceAttribute : Attribute;
