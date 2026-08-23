using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using GenWave.Host.Auth;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/announcements/token</c> (generate/regenerate, reveal-once) and
/// <c>DELETE /api/announcements/token</c> (revoke) — SPEC F145.3/.4, STORY-360, PLAN T340.
///
/// <b>SESSION ONLY — a token must never mint or revoke a token.</b> Deliberately carries a bare
/// <c>[Authorize(AuthenticationSchemes = "Cookie", Policy = AuthorizationPolicies.Operator)]</c>,
/// naming ONLY the cookie scheme — unlike <see cref="AnnouncementsController"/>, this controller never
/// lists <see cref="AnnounceTokenAuthenticationDefaults.InScopeSchemes"/>. An announce-token Bearer
/// header presented here authenticates against nothing (the "AnnounceToken" scheme is never even
/// consulted for a route that doesn't name it — see <see cref="AnnounceTokenAuthenticationDefaults"/>'s
/// own remarks) and this route falls back to requiring the admin cookie alone, so a caller holding
/// only a token can never widen its own privilege by minting a replacement or erasing the one that
/// scopes it.
/// </summary>
[ApiController]
[Route("api/announcements/token")]
[AdminSurface]
[Authorize(AuthenticationSchemes = "Cookie", Policy = AuthorizationPolicies.Operator)]
public sealed class AnnouncementTokenController(
    IAnnounceTokenStore tokenStore,
    ILogger<AnnouncementTokenController> logger) : ControllerBase
{
    /// <summary>The plaintext token's byte length (SPEC F145.3's "≥32 bytes" floor) — 256 bits of
    /// <see cref="RandomNumberGenerator"/> output, url-safe base64 encoded.</summary>
    const int TokenBytes = 32;

    /// <summary>
    /// Generates a fresh token, discarding any previous one (a regenerate — the prior plaintext's
    /// hash no longer matches anything stored, so it is refused on its very next request). The
    /// plaintext is returned in THIS response body and nowhere else, ever — never logged, never
    /// stored (only its SHA-256 hash is persisted, hex-encoded, via
    /// <see cref="IAnnounceTokenStore.SetHashAsync"/>), never echoed back by any later read.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GenerateOrRegenerate(CancellationToken ct)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(TokenBytes);
        var plaintext = WebEncoders.Base64UrlEncode(tokenBytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));

        await tokenStore.SetHashAsync(hash, ct);

        // Never logs the plaintext or the hash — only that a regenerate happened, and by whom
        // (the admin session; there is only ever one admin identity today).
        logger.LogInformation("Announce token generated/regenerated");
        return Ok(new AnnounceTokenGeneratedDto(plaintext));
    }

    /// <summary>
    /// Deletes the hash row outright (SPEC F145.4's "no hash row" fail-closed state) — every
    /// previously issued plaintext is refused on its very next Bearer request.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        await tokenStore.RevokeAsync(ct);
        logger.LogInformation("Announce token revoked");
        return NoContent();
    }

    /// <summary>
    /// <c>GET /api/announcements/token/status</c> (SPEC F146.3, STORY-361, PLAN T344) — the
    /// Announcements page's own token panel: whether a token currently exists, plus the last-used
    /// stamp <see cref="IAnnounceTokenStore.StampLastUsedAsync"/> writes on every successful Bearer
    /// authentication (PLAN T340). SESSION ONLY, same as <see cref="GenerateOrRegenerate"/>/
    /// <see cref="Revoke"/> above — this route is deliberately absent from the class remarks'
    /// <see cref="AnnounceTokenAuthenticationDefaults.InScopeSchemes"/> list too: a caller holding
    /// only a token has no more business introspecting the credential that scopes it than minting or
    /// revoking one. Never returns the hash or plaintext (<see cref="AnnounceTokenStatusDto"/>'s own
    /// remarks) — reveal-once (SPEC F145.3) stays intact.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var hash = await tokenStore.ReadHashAsync(ct);
        var lastUsedAt = await tokenStore.ReadLastUsedAsync(ct);
        return Ok(new AnnounceTokenStatusDto(hash is not null, lastUsedAt));
    }
}
