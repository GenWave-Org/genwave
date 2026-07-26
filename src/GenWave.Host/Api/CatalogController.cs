using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Catalog;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// The Persona Catalog browse surface (SPEC F90.2, STORY-234, PLAN T101): <c>GET /api/catalog/index</c>
/// and <c>GET /api/catalog/entries/{slug}</c>, the two read-only routes the Admin UI's shelf browse
/// (a later task) will consume. Every fetch/verify/cache/single-flight concern lives in
/// <see cref="CatalogProxyService"/> (SPEC F90.2-F90.4, PLAN T100) — this controller's ENTIRE job is
/// translating that service's closed-hierarchy results into HTTP, never re-deriving any of its own
/// rules.
///
/// <para>
/// POLICY PARITY (SPEC F90.2's own "same auth policy as the existing persona import endpoint" rule):
/// <see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/> — the EXACT
/// shape <c>PersonaController</c>'s <c>Import</c> action carries, since importing a catalog card
/// ends at that same action (SPEC F90.5). No rate limiting (T101 review): every other admin-plane
/// controller in this codebase carries none — rate limiting here is reserved for public/
/// unauthenticated surfaces (spectator requests, login) where an anonymous caller can hammer a route
/// for free; this one already costs a valid admin session.
/// </para>
///
/// <para>
/// DISABLED (SPEC F90.1) IS A BARE 404, NOT A PROBLEM BODY: <see cref="CommunityCatalogAccessor.IsEnabled"/>
/// false means the catalog surface itself does not exist right now — the same "reveals nothing, not
/// even that a feature flag exists" posture <see cref="AdminSurfaceAttribute"/>'s own surface-gate
/// idiom already uses for <c>Admin:Enabled</c>/<c>Station:SpectatorMode</c> (see
/// <see cref="SurfaceGateMiddleware"/>). Checked here, in-action, rather than via a THIRD
/// <c>SurfaceGateMiddleware</c> attribute+static-config-boolean pair: those two existing gates read a
/// boolean decided BEFORE routing needs to know anything about the request; this one is decided by
/// the exact same live read (<see cref="CommunityCatalogAccessor.IsEnabled"/>) the controller needs
/// anyway to know WHAT to fetch, so a parallel middleware-level copy of that same read would be
/// duplication, not reuse.
/// </para>
///
/// <para>
/// UNREACHABLE IS A GRACEFUL 200, NEVER A NON-2XX STATUS (design choice, T101): once the catalog is
/// enabled, both routes stay 200 even with a cold cache or a rejected/unreachable origin —
/// <see cref="CatalogIndexResponse.Unreachable"/>/<see cref="CatalogEntryResponse.Unreachable"/> carry
/// the signal instead. This mirrors the two existing house idioms for a degraded UI-facing read
/// (<c>SpectatorTrackNowPlaying.Listeners</c> going null when Icecast stats are unreachable;
/// <c>StatusController</c>'s <c>llm.lastOutcome</c> going null with no LLM attempt yet — both "state
/// embedded in a 200", never an HTTP error status for "nothing to show yet") AND ARCHITECTURE.md's
/// own Persona Catalog section, verbatim: "Offline/unreachable = a graceful empty-state, never an
/// error page." A 503 was the other candidate (matches <c>VoicesController</c>'s 502-on-unreachable
/// posture) but that shape fits a SINGLE external dependency a caller either gets or doesn't
/// (<c>ITtsVoiceLister</c> underpins exactly one dropdown); the catalog index is a LIST the Admin UI
/// renders as a page, and a page that 5xxs on a cold cache is precisely the "error page"
/// ARCHITECTURE.md rules out. <see cref="CatalogEntryFetchResult.Unreachable"/> reuses the SAME shape
/// at the entry route for the one narrow race it represents (the index went unreachable between page
/// load and a detail click) — deliberately distinct from <see cref="CatalogEntryFetchResult.NotFound"/>
/// (a real, durable "no such slug"), which still 404s.
/// </para>
///
/// <para>
/// HASH MISMATCH / OVERSIZE ARE 502, NOT THE GRACEFUL SHAPE (SPEC F90.3): both are the ONE case
/// where the origin answered but served something this station refuses to relay — a genuine
/// upstream-integrity failure, the textbook meaning of Bad Gateway, and different in kind from
/// "nothing to show" (an empty/cold shelf is not an error; a tampered or oversize file is). The
/// response body never echoes the slug/hash values <see cref="CatalogProxyService"/> already WARNs
/// server-side (mirrors <c>VoicesController</c>'s own "no internal detail in a 502 body" posture,
/// F15.7) — an operator reads the WARN in the logs, not the browser.
/// </para>
/// </summary>
[ApiController]
[Route("api/catalog")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed partial class CatalogController(
    CatalogProxyService catalogProxyService, CommunityCatalogAccessor catalogAccessor) : ControllerBase
{
    /// <summary>
    /// GET /api/catalog/index — the shelf listing (SPEC F90.2, F90.4). 404 (bare) when the catalog
    /// is disabled (F90.1); otherwise always 200 — see this controller's own remarks for why
    /// <see cref="CatalogIndexFetchResult.Unreachable"/> is embedded rather than a non-2xx status.
    /// </summary>
    [HttpGet("index")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!catalogAccessor.IsEnabled)
            return DisabledSurfaceResult();

        var result = await catalogProxyService.GetIndexAsync(ct);

        return result switch
        {
            CatalogIndexFetchResult.Ok ok => Ok(new CatalogIndexResponse(
                ok.Entries.Select(ToShelfEntryDto).ToArray(), ok.FetchedAt, Unreachable: false)),
            CatalogIndexFetchResult.Unreachable => Ok(new CatalogIndexResponse(null, null, Unreachable: true)),
            // CatalogIndexFetchResult's constructor is private (closed hierarchy) — this arm can
            // never actually run; kept because Roslyn's pattern-exhaustiveness checker doesn't treat
            // a private-constructor closed hierarchy as provably exhaustive (mirrors
            // CatalogProxyService's own discard arms).
            _ => throw new UnreachableException($"Unhandled {nameof(CatalogIndexFetchResult)} case."),
        };
    }

    /// <summary>
    /// GET /api/catalog/entries/{slug} — one entry's hash-verified card + meta content (SPEC F90.2,
    /// F90.3). 404 (bare) when the catalog is disabled (F90.1); 400 for an over-length or malformed
    /// slug (see <see cref="MaxSlugLength"/>/<see cref="SlugFormat"/>); 404 (with a body) for a
    /// well-formed but unknown slug; 502 when the fetched content fails its F90.3 integrity check
    /// (hash mismatch or oversize — WARN already logged by <see cref="CatalogProxyService"/>);
    /// otherwise 200, or the graceful <see cref="CatalogEntryResponse.Unreachable"/> shape — see this
    /// controller's own remarks.
    /// </summary>
    [HttpGet("entries/{slug}")]
    public async Task<IActionResult> Entry(string slug, CancellationToken ct)
    {
        if (!catalogAccessor.IsEnabled)
            return DisabledSurfaceResult();

        if (slug.Length > MaxSlugLength)
            return BadRequest(SlugTooLongProblem(slug.Length));

        if (!SlugFormat().IsMatch(slug))
            return BadRequest(BadSlugProblem(slug));

        var result = await catalogProxyService.GetEntryAsync(slug, ct);

        return result switch
        {
            CatalogEntryFetchResult.Ok ok => Ok(new CatalogEntryResponse(
                ok.Content.CardJson, ok.Content.MetaJson, ok.FetchedAt, Unreachable: false)),
            CatalogEntryFetchResult.NotFound => NotFound(UnknownEntryProblem(slug)),
            CatalogEntryFetchResult.Unreachable => Ok(new CatalogEntryResponse(null, null, null, Unreachable: true)),
            CatalogEntryFetchResult.HashMismatch =>
                StatusCode(StatusCodes.Status502BadGateway, WithheldProblem("failed its integrity check")),
            CatalogEntryFetchResult.Oversize =>
                StatusCode(StatusCodes.Status502BadGateway, WithheldProblem("exceeded its size limit")),
            // CatalogEntryFetchResult's constructor is private (closed hierarchy) — see Index's own
            // discard arm for why this still needs one.
            _ => throw new UnreachableException($"Unhandled {nameof(CatalogEntryFetchResult)} case."),
        };
    }

    static CatalogShelfEntryDto ToShelfEntryDto(CatalogEntrySummary summary) =>
        new(summary.Slug, ToWireAudience(summary.Audience), summary.BestFor);

    // Lowercase, matching genwave-catalog's own schema vocabulary verbatim — see
    // CatalogShelfEntryDto's own remarks on why this is never the enum's default PascalCase serialization.
    static string ToWireAudience(CatalogAudience audience) => audience switch
    {
        CatalogAudience.Everyone => "everyone",
        CatalogAudience.Mature => "mature",
        _ => throw new UnreachableException($"Unhandled {nameof(CatalogAudience)} value: {audience}."),
    };

    /// <summary>
    /// Length bound BEFORE the regex (T101 review — parity with <c>PersonaController.Import</c>'s
    /// own <c>MaxCatalogSlugLength</c> guard on its <c>catalogSlug</c> parameter): cheap reject, and
    /// keeps a pathological input away from the regex engine at all. A real catalog entry slug is a
    /// short, human-authored identifier (<see cref="CatalogIndexValidator.SlugSegment"/>'s own
    /// vocabulary), never anywhere near this long.
    /// </summary>
    const int MaxSlugLength = 64;

    // Composed from CatalogIndexValidator.SlugSegment — the catalog's OWN slug vocabulary (that
    // class parses the identical shape out of an untrusted index.json) — anchored \A/\z, not ^/$ (a
    // SECOND file this exact PersonaController.SlugFormat regression class could recur in: .NET's
    // regex `$` matches immediately before a trailing '\n', not just true end-of-input, so `.../\z`
    // is what actually rejects e.g. "valid-dj\n" over the wire, not `.../$`). [GeneratedRegex]
    // attribute arguments must be compile-time constants, so this recomposes the const text rather
    // than calling CatalogIndexValidator.SlugPattern() directly — the RULE lives once
    // (CatalogIndexValidator.SlugSegment); this is its one other consumer, not a second copy of it.
    [GeneratedRegex(@"\A" + CatalogIndexValidator.SlugSegment + @"\z")]
    private static partial Regex SlugFormat();

    /// <summary>
    /// The F87.2/F61 surface-off idiom (see this controller's own class remarks): a truly bare,
    /// zero-byte 404 with no <c>Content-Type</c> — <see cref="ControllerBase.NotFound()"/> looks bare
    /// at the call site, but <c>[ApiController]</c>'s automatic client-error-to-ProblemDetails
    /// conversion turns it into a JSON body regardless (confirmed empirically: <c>StatusCode(404)</c>
    /// does too — the conversion triggers on the STATUS CODE, not the result type). Setting
    /// <see cref="HttpResponse.StatusCode"/> directly and returning <see cref="EmptyResult"/> bypasses
    /// that filter entirely (empirically verified: 0 bytes, no <c>Content-Type</c> header).
    /// </summary>
    IActionResult DisabledSurfaceResult()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }

    static ProblemDetails BadSlugProblem(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"\"{slug}\" is not a valid catalog entry slug (lowercase letters, digits, and single hyphens only).",
    };

    static ProblemDetails SlugTooLongProblem(int length) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"slug must be at most {MaxSlugLength} characters (got {length}).",
    };

    static ProblemDetails UnknownEntryProblem(string slug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No catalog entry with slug \"{slug}\" exists.",
    };

    // Deliberately no slug/hash/upstream detail here (F15.7 — mirrors VoicesController's own
    // BadGateway posture): that detail is already in the WARN CatalogProxyService logs server-side.
    static ProblemDetails WithheldProblem(string reason) => new()
    {
        Status = StatusCodes.Status502BadGateway,
        Title  = "Persona catalog entry unavailable.",
        Detail = $"This entry {reason} and was withheld. Try again shortly.",
    };
}
