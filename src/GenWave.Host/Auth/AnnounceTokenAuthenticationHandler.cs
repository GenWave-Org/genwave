using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace GenWave.Host.Auth;

/// <summary>
/// The <c>"AnnounceToken"</c> authentication scheme (SPEC F145.3/.4, STORY-360, PLAN T340):
/// validates <c>Authorization: Bearer &lt;token&gt;</c> against the CURRENT hash in
/// <see cref="IAnnounceTokenStore"/> by SHA-256 + <see cref="CryptographicOperations.FixedTimeEquals"/>.
///
/// <b>Fail-closed with no hash row (SPEC F145.4).</b> <see cref="IAnnounceTokenStore.ReadHashAsync"/>
/// returning <see langword="null"/> — no token ever generated, or a prior revoke — refuses every
/// presented Bearer value; there is no "any token works" fallback state.
///
/// <b>Live per request, never cached.</b> The hash is read fresh from
/// <see cref="IAnnounceTokenStore"/> on every single authentication attempt (never at scheme
/// construction, never memoized) — a regenerate/revoke through <c>AnnouncementTokenController</c>
/// takes effect on the very next request, no api restart, honoring the "read the CURRENT hash per
/// request" carry-forward.
///
/// <b>No DB hit for a cookie-only caller.</b> When the request carries no <c>Bearer</c> Authorization
/// header at all, this handler returns <see cref="AuthenticateResult.NoResult"/> immediately, before
/// ever touching <see cref="IAnnounceTokenStore"/> — a session-authenticated announcements request
/// never pays for a settings-row read it never needed (the framework then falls through to whichever
/// other scheme the route also lists, e.g. <c>"Cookie"</c>).
///
/// <b>The scope claim, not authority.</b> A successful validation stamps exactly one claim
/// (<see cref="AnnounceTokenAuthenticationDefaults.ScopeClaimType"/> =
/// <see cref="AnnounceTokenAuthenticationDefaults.AnnouncementsScopeClaimValue"/>) — SPEC F145.4
/// grants this token exactly the announcements family, never a broader admin identity. THE FENCE
/// ITSELF is enforced structurally, by which controllers list <c>"AnnounceToken"</c> among their
/// accepted <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute.AuthenticationSchemes"/>
/// (see <see cref="AnnounceTokenAuthenticationDefaults"/>'s own remarks) — this handler succeeding
/// does not, by itself, authorize anything; ASP.NET Core's own authorization middleware only ever
/// calls this scheme's <c>AuthenticateAsync</c> for a route that named it.
///
/// <b>The accepted-rate budget is SHARED across this door and the cookie door, by design.</b> SPEC
/// F143.4's station-wide cap (<see cref="AnnouncementAcceptedRateLimiter"/>) is acquired by
/// <c>AnnouncementsController.Post</c> itself, after authentication, with no branch on WHICH scheme
/// authenticated the caller — one station, one break system, one budget for "how many announcements
/// land on air per minute" regardless of which door they came through.
///
/// <b>Last-used, stamped here.</b> <see cref="IAnnounceTokenStore.StampLastUsedAsync"/> runs once per
/// successful validation, per request (this station's traffic is low enough that a per-request write
/// costs nothing worth throttling to minute-granularity) — a failed/absent-header attempt never
/// stamps anything, so the timestamp only ever reflects a genuine successful use.
/// </summary>
public sealed class AnnounceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAnnounceTokenStore tokenStore)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var headerValues))
            return AuthenticateResult.NoResult();

        var headerValue = headerValues.ToString();
        if (!headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var presented = headerValue[BearerPrefix.Length..].Trim();
        if (presented.Length == 0)
            return AuthenticateResult.Fail("Bearer token was empty.");

        var storedHash = await tokenStore.ReadHashAsync(Context.RequestAborted);
        if (storedHash is null)
            return AuthenticateResult.Fail("No announce token is configured.");

        byte[] storedHashBytes;
        try
        {
            storedHashBytes = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            // Can only happen if the stored row was ever written by something other than
            // AnnounceTokenStore.SetHashAsync's own hex encoding — treat exactly like "no usable
            // hash" (fail-closed), never a 500.
            return AuthenticateResult.Fail("Stored announce token hash is malformed.");
        }

        var presentedHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(presented));

        if (!CryptographicOperations.FixedTimeEquals(presentedHashBytes, storedHashBytes))
            return AuthenticateResult.Fail("Invalid announce token.");

        await tokenStore.StampLastUsedAsync(Context.RequestAborted);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "announce-token"),
            new Claim(AnnounceTokenAuthenticationDefaults.ScopeClaimType, AnnounceTokenAuthenticationDefaults.AnnouncementsScopeClaimValue),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
