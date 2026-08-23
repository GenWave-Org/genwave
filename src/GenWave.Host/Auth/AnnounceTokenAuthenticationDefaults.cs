using System.Security.Claims;

namespace GenWave.Host.Auth;

/// <summary>
/// The single registration point for the <c>"AnnounceToken"</c> authentication scheme's own
/// constants (SPEC F145.3/.4, STORY-360, PLAN T340) — mirrors <c>AuthorizationPolicies</c>'s own
/// "one place, not scattered magic strings" rule for named policies, applied here to a named scheme.
///
/// <b>The scope fence, precisely.</b> <see cref="SchemeName"/> is registered as an available scheme
/// (<c>AdminApiServiceCollectionExtensions.AddGenWaveAdminApi</c>) but is never the DEFAULT
/// authentication scheme — only a controller that explicitly lists it in its own
/// <c>[Authorize(AuthenticationSchemes = ...)]</c> ever has
/// <see cref="AnnounceTokenAuthenticationHandler"/> consulted at all. <see cref="InScopeSchemes"/> is
/// that opt-in list, carried by exactly two places today: <c>AnnouncementsController</c> (the
/// announcements family, F143 + the now-playing read, F145.3) and nowhere else — every other
/// Operator-plane controller (<c>SettingsController</c> among them) keeps its bare
/// <c>[Authorize(Policy = ...)]</c>, which authenticates against the DEFAULT scheme
/// (<c>"Cookie"</c>) alone, so a Bearer header on those routes is never even inspected, let alone
/// accepted — the fence is structural (which schemes a route lists), not a per-route denial check.
/// <c>AnnouncementTokenController</c> (mint/revoke) deliberately does NOT carry
/// <see cref="InScopeSchemes"/> — session only, so a token can never mint or revoke a token.
/// </summary>
public static class AnnounceTokenAuthenticationDefaults
{
    /// <summary>The scheme name, also this identity's <see cref="ClaimsIdentity.AuthenticationType"/>.</summary>
    public const string SchemeName = "AnnounceToken";

    /// <summary>
    /// The exact <c>[Authorize(AuthenticationSchemes = ...)]</c> value for a route that accepts EITHER
    /// the admin cookie session OR the announce Bearer token — a compile-time constant (required for
    /// an attribute argument), so every in-scope controller states this literally rather than
    /// composing it at runtime.
    /// </summary>
    public const string InScopeSchemes = "Cookie," + SchemeName;

    /// <summary>The scope claim type a successful Bearer authentication stamps on its principal.</summary>
    public const string ScopeClaimType = "genwave:announce-scope";

    /// <summary>
    /// The one scope value this station's token ever carries — SPEC F145.4 grants exactly the
    /// announcements family, so there is only ever this single value, never a role hierarchy to
    /// model.
    /// </summary>
    public const string AnnouncementsScopeClaimValue = "announcements";

    /// <summary>
    /// True when <paramref name="user"/> carries an identity the Bearer scheme itself issued (as
    /// opposed to an admin cookie session) — <c>AnnouncementsController.Post</c>'s own submitter
    /// derivation (the binding "derive from the PRINCIPAL, never the body" carry-forward,
    /// <see cref="GenWave.Core.Domain.AnnouncementSubmitter"/>'s own remarks). Checks the scope CLAIM,
    /// not <see cref="ClaimsIdentity.AuthenticationType"/> — the intentional, self-documenting signal
    /// <see cref="AnnounceTokenAuthenticationHandler"/> stamps on success, rather than an implicit
    /// coupling to the scheme's own name string.
    /// </summary>
    public static bool HasAnnouncementsScope(ClaimsPrincipal user) =>
        user.HasClaim(ScopeClaimType, AnnouncementsScopeClaimValue);
}
