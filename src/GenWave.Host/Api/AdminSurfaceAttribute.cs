namespace GenWave.Host.Api;

/// <summary>
/// Endpoint metadata marking a route as part of the admin control plane (SPEC F61,
/// F62.2's surface-gate mechanics). <see cref="SurfaceGateMiddleware"/> returns a bare 404 for
/// any endpoint carrying this marker when <c>Admin:Enabled</c> is false — the surface does not
/// exist, rather than merely refusing authentication (F61.2). Applied to every admin controller,
/// including <see cref="AuthController"/> (STORY-166): the kill switch removes even the login
/// form.
///
/// Usable as a class/method attribute (<c>[AdminSurface]</c>) on MVC controllers/actions today,
/// and as plain endpoint metadata via
/// <c>RouteGroupBuilder.WithMetadata(new AdminSurfaceAttribute())</c> for minimal-API route
/// groups later.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class AdminSurfaceAttribute : Attribute;
