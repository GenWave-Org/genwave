using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using GenWave.Core.Abstractions;
using GenWave.Host.Auth;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// Authentication + station info for the single-station Admin UI.
/// Login checks the submitted password against the single configured <c>Admin:Password</c>
/// (no user table). When no password is configured (SPEC F60.4/T02, STORY-164) login always
/// fails and no cookie is ever issued — the admin plane is fail-closed, not open.
///
/// Library listing has moved to <see cref="LibrariesController"/> (STORY-047, Epic J):
/// GET /api/libraries now returns every library row (not scope-filtered) with a media count.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AuthController(
    IOptions<AdminOptions> adminOptions,
    IStationIdentityProvider identityProvider,
    ILogger<AuthController> logger) : ControllerBase
{
    const string InvalidCredentialsMessage = "Invalid credentials.";

    /// <summary>Verifies the admin password, sets the auth cookie, returns 204.</summary>
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var configured = adminOptions.Value.Password;

        // gh-#74: every login outcome logs who was at the door. RemoteIpAddress is already
        // XFF-corrected for callers transiting caddy (Proxy:TrustedNetworks, T13); the two CF
        // headers carry the real client IP and the Access-verified identity when a Cloudflare
        // tunnel fronts the plane — both empty ("-") on LAN paths, which is itself the signal
        // that the caller bypassed Access.
        //
        // CodeQL cs/exposure-of-sensitive-information fires on logging the email — that
        // disclosure is deliberate and IS this endpoint's audit contract: attributing admin
        // login attempts to an identity is the point (gh-#74 exists because a real triage
        // could not). The sink is the operator's own log pipeline, not a user-facing response;
        // masking would reduce the audit log below its reason to exist. Alerts dismissed
        // as won't-fix with this rationale.
        var remote = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cfConnectingIp = SanitizeHeader(HttpContext.Request.Headers["CF-Connecting-IP"]);
        var accessUser = SanitizeHeader(HttpContext.Request.Headers["Cf-Access-Authenticated-User-Email"]);

        // No configured password = fail-closed (SPEC F60.4): login can never succeed, so no
        // cookie is ever issued. Otherwise require a constant-time match.
        if (string.IsNullOrEmpty(configured) || !FixedTimeEquals(request.Password, configured))
        {
            logger.LogWarning(
                "Login failed: wrong admin password (remote: {RemoteIp}, cf-connecting-ip: {CfConnectingIp}, access-user: {AccessUser})",
                remote, cfConnectingIp, accessUser);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "admin") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"));
        await HttpContext.SignInAsync("Cookie", principal, new AuthenticationProperties { IsPersistent = true });
        logger.LogInformation(
            "Admin login succeeded (remote: {RemoteIp}, cf-connecting-ip: {CfConnectingIp}, access-user: {AccessUser})",
            remote, cfConnectingIp, accessUser);
        return NoContent();
    }

    /// <summary>
    /// Header values are caller-controlled: newline-strip them so a crafted header can't forge
    /// additional log entries (CodeQL cs/log-forging — same rule as LlmCopyWriter's persona
    /// fields), and collapse absent to "-" so the log line shape is constant.
    /// </summary>
    static string SanitizeHeader(StringValues value) =>
        StringValues.IsNullOrEmpty(value) ? "-" : value.ToString().ReplaceLineEndings(" ");

    /// <summary>Clears the auth cookie, returns 204.</summary>
    [HttpPost("auth/logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookie");
        return NoContent();
    }

    /// <summary>Identity payload for the Admin UI; the single station is always visible.</summary>
    [HttpGet("auth/me")]
    public IActionResult Me() =>
        Ok(new { username = User.Identity?.Name ?? "admin", stations = new[] { SingleStation.Id } });

    /// <summary>
    /// Lists stations — exactly one in a single-station deployment. Reads the live-effective name
    /// through <see cref="IStationIdentityProvider"/> on every call (SPEC F44.6, gitea-#196) — a
    /// <c>Station:Name</c> settings edit is visible on the very next call, no api restart.
    /// </summary>
    [HttpGet("stations")]
    public IActionResult Stations() =>
        Ok(new[] { new StationDto(SingleStation.Id, identityProvider.Current.Name) });

    static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
