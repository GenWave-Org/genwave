namespace GenWave.Host.Api;

/// <summary>
/// Endpoint metadata marking the listener-request intake route (SPEC F87.2, F61's surface-gate
/// mechanics; STORY-224, PLAN T87). <see cref="SurfaceGateMiddleware"/> returns a bare 404 for any
/// endpoint carrying this marker when <c>Station:Requests:Enabled</c> is false — the same
/// "the route does not exist" contract as <see cref="AdminSurfaceAttribute"/>/
/// <see cref="SpectatorSurfaceAttribute"/>, and checked INDEPENDENTLY of
/// <see cref="SpectatorSurfaceAttribute"/>'s own <c>Station:SpectatorMode</c> gate: an operator can
/// run the public spectator surface with requests specifically switched off.
///
/// This check runs in the same middleware pass as the other two surface gates — before
/// <c>UseRateLimiter</c> in the pipeline (see <c>Program.cs</c>) — so a disabled kill switch 404s
/// even under a flood; it can never surface as a 429, which would itself be a live/dead-feature
/// oracle for a public, unauthenticated endpoint.
///
/// Usable as a class/method attribute (<c>[RequestsSurface]</c>) on MVC controllers/actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class RequestsSurfaceAttribute : Attribute;
