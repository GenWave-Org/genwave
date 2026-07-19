using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
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

        // No configured password = fail-closed (SPEC F60.4): login can never succeed, so no
        // cookie is ever issued. Otherwise require a constant-time match.
        if (string.IsNullOrEmpty(configured) || !FixedTimeEquals(request.Password, configured))
        {
            logger.LogWarning("Login failed: wrong admin password");
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "admin") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"));
        await HttpContext.SignInAsync("Cookie", principal, new AuthenticationProperties { IsPersistent = true });
        return NoContent();
    }

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
