using Microsoft.Extensions.Options;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// Stamps the browser security headers (gh-#180) — <c>Content-Security-Policy</c>,
/// <c>X-Frame-Options</c>, <c>Referrer-Policy</c>, <c>X-Content-Type-Options</c> — on every
/// response whose matched endpoint carries <see cref="SpectatorSurfaceAttribute"/>: the public
/// page, its static assets, and <c>/spectator/api/*</c> (headers on API JSON are harmless
/// and keep the surface uniform). Keyed on endpoint metadata, never on path shape, so a future
/// spectator route is covered by construction and the admin plane, <c>/media/*</c>, and
/// <c>/internal/*</c> are never touched. The vendored fonts (PLAN T173, <c>GET /fonts/{file}</c>)
/// are deliberately NOT among these routes — they are shared with admin and unattributed by
/// design (see <c>FontEndpoints</c>'/<c>SurfaceGateMiddleware</c>'s own remarks) — but that costs
/// nothing here: CSP is a document-level policy the BROWSER enforces against the page that
/// declared it, not a per-response header a sub-resource fetch needs of its own, so an unheadered
/// same-origin font response is still governed by the page's own <c>font-src 'self'</c>.
///
/// <para>
/// Pipeline position (see <c>Program.cs</c>): after <c>UseRouting</c> (needs the matched
/// endpoint's metadata) and after <see cref="SurfaceGateMiddleware"/> — the gate short-circuits a
/// disabled surface before this middleware runs, so its bare 404 stays byte-identical to an
/// unmapped route's (F61.2's no-oracle discipline; stamping headers on it would fingerprint the
/// surface's existence). Before the rate limiter/auth/OutputCache, so 429s and error responses on
/// a live surface carry the same headers as a 200. Headers are written before <c>next</c> runs,
/// which also lands them inside OutputCache entries — a cache hit replays the CSP captured at
/// store time, so a live <c>Station:PublicBaseUrl</c> edit reaches cached routes within their
/// policy TTL (≤300s), the same bounded staleness their bodies already have.
/// </para>
///
/// <para>
/// The CSP is emitted per request, not captured at startup: <c>Station:PublicBaseUrl</c> and
/// <c>Station:PublicStreamUrl</c> are LIVE-apply settings (StationSettingsAllowlist), so both are
/// read through <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every request — the same
/// live-read rule <see cref="SurfaceGateMiddleware"/> follows.
/// </para>
///
/// <para>
/// Internal, not public: <c>UseMiddleware</c> needs no public type, and Story-183's disclosure
/// contract pins every public <c>Spectator*</c> type in this namespace to a blessed list —
/// middleware is plumbing, not a wire shape, so it stays out of the public surface entirely.
/// </para>
/// </summary>
sealed class SpectatorSecurityHeadersMiddleware(
    RequestDelegate next,
    IOptionsMonitor<StationOptions> stationOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SpectatorSurfaceAttribute>() is not null)
        {
            var station = stationOptions.CurrentValue;
            var headers = context.Response.Headers;

            headers.ContentSecurityPolicy = BuildContentSecurityPolicy(
                station.PublicBaseUrl, station.PublicStreamUrl);
            // Legacy twin of frame-ancestors 'none' below, for clients that predate CSP2.
            headers.XFrameOptions = "DENY";
            // The page links out (the about panel's source anchor): send origin only, and only
            // downgrade-safe — never the full URL to a third party.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            // Assets are served with explicit content types (SpectatorPageEndpoints); never let a
            // browser second-guess them into something executable.
            headers.XContentTypeOptions = "nosniff";
        }

        await next(context);
    }

    /// <summary>
    /// Composes the spectator CSP from the page's actual load inventory (index.html + styles.css
    /// + app.js, audited for gh-#180) — every directive below is justified by something the page
    /// really does, and nothing else is allowed:
    /// <list type="bullet">
    /// <item><c>default-src 'none'</c> — deny by default; every fetch class the page performs is
    /// enumerated explicitly below, so anything new fails loudly instead of leaking open.</item>
    /// <item><c>base-uri 'none'</c> — the page has no <c>&lt;base&gt;</c> tag; forbid injecting
    /// one to re-root the absolute /spectator/* references.</item>
    /// <item><c>form-action 'self'</c> — the request form submits via fetch (app.js intercepts
    /// submit); 'self' keeps the native no-JS fallback working while blocking cross-origin
    /// exfiltration targets. Not covered by default-src, so it must be explicit.</item>
    /// <item><c>frame-ancestors 'none'</c> — the page is never embedded; kills clickjacking
    /// (X-Frame-Options: DENY is the legacy twin).</item>
    /// <item><c>script-src 'self'</c> — exactly one script, /spectator/app.js; no inline scripts
    /// or handlers. Also the belt on the wire-supplied <c>about.projectUrl</c> anchor href sink:
    /// without 'unsafe-inline' a <c>javascript:</c> URL will not execute on click.</item>
    /// <item><c>style-src 'self'</c> — one stylesheet, /spectator/styles.css; no inline
    /// &lt;style&gt; or style= attributes anywhere. app.js drives the progress bar through the
    /// CSSOM (<c>element.style.width</c>), which style-src does not govern — so no
    /// 'unsafe-inline' is needed and none is granted.</item>
    /// <item><c>font-src 'self'</c> — vendored woff2 under /fonts (design-aesthetic rule: never a
    /// font CDN; PLAN T173 moved these from /spectator/fonts to the canonical shared route, the
    /// same-origin 'self' match is unaffected).</item>
    /// <item><c>img-src 'self' + PublicBaseUrl origin</c> — the favicon/station icon are
    /// same-origin, but per-track artwork (SPEC F93.3, since v2.8.0) is
    /// <c>{PublicBaseUrl}/spectator/api/artwork/{token}</c>, cross-origin whenever
    /// <c>Station:PublicBaseUrl</c> names the public host while the page is reached another
    /// way.</item>
    /// <item><c>media-src 'self' + PublicStreamUrl origin</c> — the audio element plays
    /// <c>Station:PublicStreamUrl</c> verbatim (about payload; app.js also reattaches it with a
    /// reconnect query param, same origin). Root-relative (<c>/stream</c> behind Caddy) is
    /// same-origin; an absolute URL (direct Icecast) needs its origin pinned.</item>
    /// <item><c>connect-src 'self'</c> — fetch() only ever targets /spectator/api/*.</item>
    /// </list>
    /// </summary>
    static string BuildContentSecurityPolicy(string publicBaseUrl, string publicStreamUrl) =>
        "default-src 'none'; " +
        "base-uri 'none'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "script-src 'self'; " +
        "style-src 'self'; " +
        "font-src 'self'; " +
        $"img-src {SourceList(publicBaseUrl)}; " +
        $"media-src {SourceList(publicStreamUrl)}; " +
        "connect-src 'self'";

    /// <summary>
    /// <c>'self'</c> plus the configured URL's origin, or <c>'self'</c> alone when the setting
    /// pins nothing — fail-closed: empty, unparseable, or non-http(s) config narrows the policy,
    /// never widens it, and never throws (these are operator-supplied live settings; env-supplied
    /// values bypass SettingValidator, so this guard cannot rely on it).
    /// </summary>
    static string SourceList(string configuredUrl) =>
        TryParseHttpOrigin(configuredUrl) is { } origin ? $"'self' {origin}" : "'self'";

    /// <summary>
    /// Parses an operator-configured URL down to its scheme+host+port origin for a CSP source
    /// list, or null when there is nothing safe to pin. http/https only: on Unix a root-relative
    /// value like <c>/stream</c> parses as an absolute <c>file://</c> URI, and a hostile setting
    /// could carry any scheme — both fall out here, leaving 'self'.
    /// <see cref="Uri.GetLeftPart(UriPartial)"/> omits default ports, matching CSP host-source
    /// matching semantics.
    /// </summary>
    static string? TryParseHttpOrigin(string configuredUrl)
    {
        if (Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return null;
    }
}
