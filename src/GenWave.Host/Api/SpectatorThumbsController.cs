using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Playout;
using GenWave.MediaLibrary.Options;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /spectator/api/thumbs</c> — the Library Gardener's public taste-thumb intake (SPEC
/// F150.2–F150.7, STORY-369, PLAN T358/T365/T366) — the codebase's SECOND public anonymous WRITE
/// endpoint, built on the <see cref="SpectatorRequestsController"/> precedent one seam over: its own
/// kill switch (<see cref="ThumbsSurfaceAttribute"/>, independent of
/// <see cref="SpectatorSurfaceAttribute"/>'s <c>Station:SpectatorMode</c>), its own dedicated
/// rate-limiter budget (<see cref="RateLimiterPolicies.Thumbs"/>), and the same
/// <see cref="AuthorizationPolicies.Spectator"/> policy every spectator endpoint carries.
///
/// <para>
/// <b>No-oracle discipline (SPEC F150.3).</b> The 202 body (<see cref="SpectatorThumbAccepted"/>) is
/// byte-identical whether <see cref="SpectatorThumbSubmission.Airing"/> named the current airing, the
/// immediately previous one (SPEC F150.4's grace), or resolved to nothing at all — a caller can never
/// distinguish "recorded" from "safe-scope-excluded" from "stale/gibberish token" from the response.
/// This is DIFFERENT from a malformed BODY (missing/over-length/wrong-charset <c>airing</c>, an
/// unrecognised <c>direction</c>) — that is a genuine contract violation on the REQUEST shape, 400,
/// the same F87.3 "wish over length ⇒ 400" posture <see cref="SpectatorRequestsController"/> already
/// establishes; a well-formed token that simply fails to resolve is F150.3's silent-202 territory.
/// </para>
///
/// <para>
/// <b>Gate order, every gate before the write it protects:</b> the surface kill switch and the
/// per-IP <see cref="RateLimiterPolicies.Thumbs"/> chain both run UPSTREAM of this action
/// (<see cref="ThumbsSurfaceAttribute"/> in <see cref="SurfaceGateMiddleware"/>,
/// <see cref="EnableRateLimitingAttribute"/> in the pipeline) — this method only ever runs for an
/// enabled, not-yet-IP-throttled caller. From there: body validation (400) → listener identity
/// (<see cref="ResolveListenerKey"/>, minting the cookie on the caller's first thumb) → token
/// resolution (T366 review MED-1: this runs BEFORE the per-listener DB read below — both a
/// resolvable and an unresolvable token answer the SAME 202, so there is nothing to gain by paying a
/// round trip before the free, in-memory <see cref="IAiringTokenResolver.TryResolve"/> has already
/// had its say; a garbage token costs this endpoint NO database access at all) → the F150.5
/// PER-LISTENER daily cap (429, acquired the SAME in-action shape
/// <c>AnnouncementsController.Post</c>'s own accepted-rate cap uses — a middleware policy cannot see
/// a listener identity a request may not carry a cookie for yet) → <see cref="IThumbStore.RecordAsync"/>,
/// whose own <c>Recorded</c>/<c>Unchanged</c>/<c>Flipped</c>/<c>Ignored</c> result (safe-scope
/// exclusion included, gh-#99 — see that method's own remarks) is NEVER inspected here: every outcome
/// answers the SAME 202.
/// </para>
///
/// <para>
/// <b>Nothing about the listener ever reaches a log line (SPEC F150.6).</b> Neither the raw cookie
/// token nor the derived <c>listener_key</c> hash is logged, echoed, or returned anywhere in this
/// class — the <see cref="AiringTokenRing"/> precedent applied to a second secret-shaped value.
/// </para>
/// </summary>
[ApiController]
[Route("spectator/api")]
[SpectatorSurface]
[ThumbsSurface]
[Authorize(Policy = AuthorizationPolicies.Spectator)]
[EnableRateLimiting(RateLimiterPolicies.Thumbs)]
public sealed partial class SpectatorThumbsController(
    IThumbStore thumbStore,
    IAiringTokenResolver airingTokenResolver,
    IOptions<GardenerOptions> gardenerOptions) : ControllerBase
{
    /// <summary>The listener-identity cookie name (SPEC F150.6).</summary>
    internal const string ListenerCookieName = "genwave-listener";

    /// <summary>128 bits (SPEC F150.6) — the same <see cref="AiringTokenRing"/> token-size precedent.</summary>
    const int ListenerTokenBytes = 16;

    /// <summary>
    /// <c>airing</c>'s own maximum length (T366's own choice, SPEC leaves this bound unstated): a
    /// real <see cref="AiringTokenRing"/> token is exactly 22 base64url chars (128 bits, unpadded) —
    /// 64 is generous headroom for that shape while still rejecting an obviously-oversized value
    /// before it ever reaches <see cref="IAiringTokenResolver.TryResolve"/>.
    /// </summary>
    const int MaxAiringLength = 64;

    /// <summary>
    /// T366 review LOW-1 (the CodeQL log-forging house trap): <c>\A</c>/<c>\z</c>, NOT <c>^</c>/<c>$</c>
    /// — .NET regex <c>$</c> matches immediately before a trailing <c>\n</c> too, so <c>^...$</c> would
    /// accept <c>"AAAA\n"</c> as a clean base64url value. <c>[GeneratedRegex]</c>, not
    /// <c>RegexOptions.Compiled</c> — the <c>ThemeManifestParser</c>/<c>SettingValidator</c> idiom this
    /// codebase already uses everywhere else a request-shaped value gets an anchored charset check.
    /// </summary>
    [GeneratedRegex(@"\A[A-Za-z0-9_-]+\z")]
    private static partial Regex Base64UrlCharsetPattern();

    /// <summary>See the class remarks for the full gate order.</summary>
    [HttpPost("thumbs")]
    [Consumes("application/json")]
    // The SpectatorRequestsController.PostRequest precedent, one seam over: an anonymous write
    // should never buffer Kestrel's ~28MB default before this action's own length checks get their
    // say. 8KB fits any legal body (an airing token up to MaxAiringLength plus a direction literal
    // plus JSON punctuation) with generous headroom.
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> PostThumb([FromBody] SpectatorThumbSubmission submission, CancellationToken ct)
    {
        var airing = submission.Airing;
        if (string.IsNullOrEmpty(airing) || airing.Length > MaxAiringLength || !Base64UrlCharsetPattern().IsMatch(airing))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid airing.",
                Detail = $"airing must be a non-empty base64url string of at most {MaxAiringLength} characters.",
            });
        }

        if (!TryParseDirection(submission.Direction, out var direction))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid direction.",
                Detail = "direction must be exactly \"up\" or \"down\".",
            });
        }

        var listenerKey = ResolveListenerKey();

        // T366 review MED-1 — resolved BEFORE the per-listener DB read below: both a resolvable and
        // an unresolvable token answer the identical 202 (SPEC F150.3/F150.4), so checking the FREE,
        // in-memory ring first means a garbage/gibberish token (the overwhelmingly common case for an
        // anonymous, unauthenticated route) never costs this endpoint a database round trip at all.
        if (!airingTokenResolver.TryResolve(airing, out var mediaId, out var startedAt))
            return Accepted(new SpectatorThumbAccepted());

        // F150.5's PER-LISTENER daily cap — acquired here, in-action, after body validation, listener
        // identity, and token resolution, BEFORE any write: the AnnouncementsController.Post precedent
        // (class remarks). A count over library.media_thumb, exact and restart-safe.
        var dailyCap = gardenerOptions.Value.ThumbDailyCap;
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var countToday = await thumbStore.CountByListenerSinceAsync(listenerKey, since, ct);
        if (countToday >= dailyCap)
            return StatusCode(StatusCodes.Status429TooManyRequests);

        // IThumbStore.RecordAsync applies the gh-#99 safe-scope exclusion and unknown-media check
        // itself (T365) — its Ignored/Recorded/Unchanged/Flipped result is deliberately never
        // inspected here; every outcome answers the same 202 (class remarks).
        await thumbStore.RecordAsync(mediaId, startedAt, listenerKey, direction, ThumbSource.Spectator, ct);

        return Accepted(new SpectatorThumbAccepted());
    }

    static bool TryParseDirection(string? value, out ThumbDirection direction)
    {
        switch (value)
        {
            case "up":
                direction = ThumbDirection.Up;
                return true;
            case "down":
                direction = ThumbDirection.Down;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    /// <summary>
    /// Reads the <see cref="ListenerCookieName"/> cookie, minting and appending a fresh one when
    /// absent or malformed (SPEC F150.6) — never logged, never returned; only the derived
    /// <c>listener_key</c> hash below leaves this method.
    /// </summary>
    string ResolveListenerKey()
    {
        var existing = Request.Cookies[ListenerCookieName];
        var token = IsValidListenerToken(existing) ? existing : MintListenerToken();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    string MintListenerToken()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(ListenerTokenBytes));

        Response.Cookies.Append(ListenerCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // T366 review MED-3: Secure follows Request.IsHttps, which the ForwardedHeaders
            // middleware only ever sets true from an X-Forwarded-Proto header sent by a hop inside
            // Proxy:TrustedNetworks (Program.cs) — see that wiring's own remarks for the demo
            // topology (cloudflared -> Caddy) this exists for.
            Secure = Request.IsHttps,
            Path = "/spectator",
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true,
        });

        return token;
    }

    static bool IsValidListenerToken([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        try
        {
            return WebEncoders.Base64UrlDecode(value).Length == ListenerTokenBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
