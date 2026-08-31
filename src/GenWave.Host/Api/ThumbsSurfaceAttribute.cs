namespace GenWave.Host.Api;

/// <summary>
/// Endpoint metadata marking the spectator taste-thumb intake route (SPEC F150.2, F61's
/// surface-gate mechanics; STORY-369, PLAN T366) — the exact <see cref="RequestsSurfaceAttribute"/>
/// precedent, one seam over. <see cref="SurfaceGateMiddleware"/> returns a bare 404 for any endpoint
/// carrying this marker when <c>Station:Thumbs:Enabled</c> is false — the same "the route does not
/// exist" contract as <see cref="AdminSurfaceAttribute"/>/<see cref="SpectatorSurfaceAttribute"/>/
/// <see cref="RequestsSurfaceAttribute"/>, and checked INDEPENDENTLY of
/// <see cref="SpectatorSurfaceAttribute"/>'s own <c>Station:SpectatorMode</c> gate: an operator can
/// run the public spectator surface with thumbs specifically switched off.
///
/// This check runs in the same middleware pass as the other surface gates — before
/// <c>UseRateLimiter</c> in the pipeline (see <c>Program.cs</c>) — so a disabled kill switch 404s
/// even under a flood; it can never surface as a 429, which would itself be a live/dead-feature
/// oracle for a public, unauthenticated endpoint.
///
/// Named "Thumbs*", not "Spectator*" — deliberately, mirroring <see cref="RequestsSurfaceAttribute"/>'s
/// own remarks: it falls outside Story183_DisclosureContractCompleteness.cs's Spectator-prefix scan,
/// so it needs no entry there (unlike <see cref="SpectatorThumbsController"/> and its DTOs, which do).
///
/// Usable as a class/method attribute (<c>[ThumbsSurface]</c>) on MVC controllers/actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class ThumbsSurfaceAttribute : Attribute;
