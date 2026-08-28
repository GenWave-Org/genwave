using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Auth;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/announcements</c> (the House Voice's front door) and <c>GET /api/announcements</c>
/// (the history read, SPEC F146.2) — SPEC F143.1/.4/.5, F145.1's endpoint half, F145.3/.4's token
/// door; STORY-357, STORY-359, STORY-360, STORY-361; PLAN T339/T340/T344. The now-playing read
/// (<c>GET /api/announcements/now-playing</c>) moved to its own <see cref="AnnouncementNowPlayingController"/>
/// at T351 (SPEC F145.6, STORY-366) — see that class's own remarks for why it needed to leave this
/// controller's <see cref="AdminSurfaceAttribute"/> gate behind. Accepts EITHER the admin cookie
/// session OR the announce Bearer token (<see cref="AnnounceTokenAuthenticationDefaults.InScopeSchemes"/>)
/// — the Operator plane, the same "keeping the station on air" grouping
/// <see cref="SafeSegmentsController"/>/<see cref="TtsPreviewController"/>/<see cref="VoicesController"/>
/// already share (an announcement is content headed for air, exactly like a safe-loop clip or a TTS
/// preview), now widened to whichever of the two auth doors SPEC F145.4 grants the announcements
/// family to. <see cref="AnnouncementTokenController"/> (mint/revoke/status) deliberately does NOT
/// share this scheme list — session only, so a token can never mint, revoke, or introspect a token.
///
/// <para>
/// <b>Per-IP door limiter (PLAN T340 carry-forward, built here, still applied post-T351).</b>
/// <see cref="EnableRateLimitingAttribute"/> carries <see cref="RateLimiterPolicies.Announcements"/> —
/// a middleware-level, per-source-IP fixed window applied to EVERY action on this controller, running
/// BEFORE authentication (<c>Program.cs</c>'s own pipeline ordering). This is a DIFFERENT budget from
/// the in-action accepted-rate cap described below: it exists purely to bound the unauthenticated
/// credential-check DB read (<see cref="AnnounceTokenAuthenticationHandler"/>'s own
/// <see cref="IAnnounceTokenStore.ReadHashAsync"/> call fires on EVERY Bearer attempt, valid or not)
/// against a junk-Bearer flood from one source, generously windowed so it never bites the HA sensor's
/// own ≥30s polling cadence or a legitimate UI session — see <see cref="RateLimiterPolicies"/>'s own
/// remarks for the full rationale and why this does not repeat T339 review finding F1's mistake.
/// <see cref="AnnouncementNowPlayingController"/> carries the SAME policy independently (its own
/// class carries the attribute, since it is no longer an action on this class) — see that class's
/// own remarks for why T351 kept it there too.
/// </para>
///
/// <para>
/// <b>Gate order (F143.4/F145.1, all enforced BEFORE any row write):</b> SpectatorMode (403, F145.1's
/// privacy hard rule — checked first, so a public station never even has its message content
/// inspected) → message required/blank (400) → message length (400, SPEC F143.4) → voice length (400,
/// T339 review finding F2) → <c>ttlSeconds</c> bounds (400, SPEC F143.1's 60–3600s) → pending depth
/// (429, SPEC F143.4 — a store read, so it runs after every purely synchronous check) → the
/// accepted-rate cap (429, SPEC F143.4, T339 review finding F1) → the store's own collapse-aware
/// insert. The accepted-rate cap is acquired HERE, in-action, via
/// <see cref="AnnouncementAcceptedRateLimiter"/> — immediately before the write it protects, and only
/// after every refusal gate above has already let the request through — never by a rate-limiter
/// MIDDLEWARE policy upstream of this action: see that type's own remarks for why (an
/// unauthenticated/refused caller must never spend a permit from this budget). <b>This budget is
/// SHARED across the session and token doors, by design</b> (PLAN T340 carry-forward) — one station,
/// one break system, one accepted-rate ceiling regardless of which door a caller authenticated
/// through; <see cref="Post"/> never branches this acquire on the authenticated principal.
/// </para>
///
/// <para>
/// <b>Collapse delegation (SPEC F143.5).</b> This action never re-implements the case-folded-duplicate
/// check — <see cref="IAnnouncementStore.InsertOrCollapseAsync"/> owns that decision entirely (the
/// T337-reviewed SQL), and <see cref="Post"/> never learns whether a given call created a fresh row or
/// folded into an existing one (<see cref="AnnouncementAcceptedDto"/>'s own remarks).
/// </para>
///
/// <para>
/// <b>Source is derived from the authenticated principal, never the request body</b> (T337 review
/// carry-forward, <see cref="AnnouncementSubmitter"/>'s own remarks) — <see cref="AnnouncementRequest"/>
/// carries no <c>source</c>/<c>submitter</c> field for a caller to spoof. PLAN T340 makes this real:
/// <see cref="Post"/> reads <see cref="AnnounceTokenAuthenticationDefaults.HasAnnouncementsScope"/>
/// off <see cref="ControllerBase.User"/> — the scope claim <see cref="AnnounceTokenAuthenticationHandler"/>
/// stamps only on a genuine Bearer success — never anything client-supplied, so a session caller can
/// never claim the token door by sending a crafted body field.
/// </para>
///
/// <para>
/// <b>TOCTOU on the pending-depth gate (T339 review carry-forward).</b> <see cref="Post"/> counts
/// pending rows, THEN inserts — two requests racing between that count and their own insert can each
/// observe the depth cap not yet reached and both write, so a station sitting at 11 pending can
/// momentarily land at 13 rather than being held at 12. Deliberately left open, not closed with a
/// locking read or a DB-side check: SPEC F143.4's depth cap exists to bound a runaway queue, not to
/// enforce an exact ceiling, and the accepted-rate cap immediately below already bounds how fast new
/// rows can arrive regardless — a momentary one-or-two-row overshoot is harmless at this station's
/// traffic shape.
/// </para>
/// </summary>
[ApiController]
[Route("api/announcements")]
[AdminSurface]
[Authorize(AuthenticationSchemes = AnnounceTokenAuthenticationDefaults.InScopeSchemes, Policy = AuthorizationPolicies.Operator)]
[EnableRateLimiting(RateLimiterPolicies.Announcements)]
public sealed class AnnouncementsController(
    IAnnouncementStore announcementStore,
    AnnouncementAcceptedRateLimiter acceptedRateLimiter,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOptionsMonitor<AnnouncementsOptions> announcementsMonitor,
    ILogger<AnnouncementsController> logger) : ControllerBase
{
    // SPEC F143.1's fixed per-request override bound — not settings-tunable (unlike the F143.4 caps
    // on AnnouncementsOptions): the SPEC states this range as a fixed law, never as a shipped default.
    const int MinTtlSeconds = 60;
    const int MaxTtlSeconds = 3600;

    // T339 review finding F2 — bounds the voice field the same honest-400 way message/ttl are
    // already bounded. Not settings-tunable (mirrors MinTtlSeconds/MaxTtlSeconds immediately above):
    // a generous fixed ceiling for a value that is only ever a short persona/voice slug, never
    // free-form prose.
    const int MaxVoiceChars = 64;

    // The store's own DDL CHECK bound (db/40) — see StoreMessageCapProblem's own remarks (T339 review
    // finding F3) for why this is named separately from AnnouncementsOptions.MessageMaxChars.
    const int StoreMessageMaxChars = 280;

    // GetHistory's own caps (SPEC F146.2, PLAN T344, T337 review's unbounded-limit carry-forward) —
    // fixed, not settings-tunable (mirrors MinTtlSeconds/MaxTtlSeconds/MaxVoiceChars above): the page
    // has no reason to ever ask for more than a couple hundred rows.
    const int DefaultHistoryLimit = 50;
    const int MaxHistoryLimit = 200;

    /// <summary>See the class remarks for the full gate order.</summary>
    [HttpPost]
    [Consumes("application/json")]
    // T339 review finding F2 (defense-in-depth): an authenticated write should never buffer Kestrel's
    // ~28MB default before this action's own length checks get their say — mirrors
    // SpectatorRequestsController.PostRequest's own precedent one seam over. 8KB fits any legal body
    // (a message up to MessageMaxChars + a voice up to MaxVoiceChars + JSON punctuation) with
    // generous headroom.
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Post([FromBody] AnnouncementRequest request, CancellationToken ct)
    {
        // F145.1 — checked first, before this action even looks at the message: a public station
        // structurally never processes house-event content, let alone stores it.
        if (stationMonitor.CurrentValue.SpectatorMode)
        {
            logger.LogWarning("Announcement refused reason=spectator-mode");
            return StatusCode(StatusCodes.Status403Forbidden, SpectatorModeProblem());
        }

        // Read once per call (reviewer-named carry-forward) — every gate below sees the SAME
        // snapshot, so a live PUT mid-request can never let one gate see an old cap and another see
        // a new one.
        var settings = announcementsMonitor.CurrentValue;

        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            return BadRequest(BlankMessageProblem());

        if (message.Length > settings.MessageMaxChars)
            return BadRequest(MessageTooLongProblem(settings.MessageMaxChars));

        var voice = string.IsNullOrWhiteSpace(request.Voice) ? null : request.Voice.Trim();
        if (voice is { Length: > MaxVoiceChars })
            return BadRequest(VoiceTooLongProblem());

        TimeSpan? ttl = null;
        if (request.TtlSeconds is { } ttlSeconds)
        {
            if (ttlSeconds < MinTtlSeconds || ttlSeconds > MaxTtlSeconds)
                return BadRequest(TtlOutOfBoundsProblem());

            ttl = TimeSpan.FromSeconds(ttlSeconds);
        }

        var pendingCount = await announcementStore.CountPendingAsync(ct);
        if (pendingCount >= settings.PendingDepthCap)
        {
            logger.LogWarning("Announcement refused reason=pending-depth-cap pendingCount={PendingCount} cap={Cap}", pendingCount, settings.PendingDepthCap);
            return StatusCode(StatusCodes.Status429TooManyRequests, DepthCapProblem(settings.PendingDepthCap));
        }

        // T339 review finding F1 — the accepted-rate cap, acquired here: after every refusal gate
        // above, immediately before the write it protects. See AnnouncementAcceptedRateLimiter's own
        // remarks for why this replaced the former rate-limiter middleware policy. Acquiring here,
        // BEFORE the insert, is an accepted trade: a permit can still be burned by a post-acquire
        // store decline/throw, but acquiring AFTER the insert would let N concurrent writes race past
        // this cap before it ever limits anything — worst case here is at most
        // settings.AcceptedPerMinute (≤6 by default) wasted permits/min, on a station already refusing
        // everything anyway.
        if (!acceptedRateLimiter.TryAcquire())
        {
            logger.LogWarning("Announcement refused reason=accepted-rate-cap cap={Cap}", settings.AcceptedPerMinute);
            return StatusCode(StatusCodes.Status429TooManyRequests, AcceptedRateCapProblem(settings.AcceptedPerMinute));
        }

        // PLAN T340 — derived from the PRINCIPAL (the scope claim AnnounceTokenAuthenticationHandler
        // stamps only on a genuine Bearer success), never the request body: see this class's own
        // remarks and AnnouncementSubmitter's own remarks for the binding rule.
        var submitter = AnnounceTokenAuthenticationDefaults.HasAnnouncementsScope(User)
            ? AnnouncementSubmitter.Token
            : AnnouncementSubmitter.Session;

        var id = await announcementStore.InsertOrCollapseAsync(
            message, request.Verbatim ?? false, voice, submitter, ttl, ct);

        // The store's own 280-char CHECK backstop (T337 review carry-forward) — unreachable in
        // practice (this action already validated length above against settings.MessageMaxChars),
        // but never a raw 500 if it ever is. A DISTINCT reason from MessageTooLongProblem (T339
        // review finding F3): the store's own hard limit is the DDL's fixed StoreMessageMaxChars
        // (db/40), which can diverge from whatever settings.MessageMaxChars is currently configured
        // to — an honest reason names the limit that ACTUALLY declined the write, not the endpoint's
        // own (already-passed) one.
        if (id is null)
            return BadRequest(StoreMessageCapProblem());

        logger.LogInformation("Announcement accepted id={Id} verbatim={Verbatim}", id, request.Verbatim ?? false);
        return Ok(new AnnouncementAcceptedDto(id.Value));
    }

    /// <summary>
    /// <c>GET /api/announcements</c> (SPEC F146.2, STORY-361, PLAN T344) — the history read the
    /// Announcements page's list renders: newest first, every state F143.2's total machine can reach,
    /// with the decline reason/collapse count/aired timestamp the visible-decline law promises. No
    /// parallel read path — this delegates entirely to <see cref="IAnnouncementStore.HistoryAsync"/>
    /// (the same store <see cref="Post"/> writes through), never a second query built here.
    ///
    /// <para>
    /// <b><paramref name="limit"/> is capped HERE, not left to the store (T337 review's own
    /// unbounded-limit carry-forward).</b> Omitted or non-positive ⇒ <see cref="DefaultHistoryLimit"/>
    /// (50); anything above <see cref="MaxHistoryLimit"/> (200) clamps down rather than 400ing — a
    /// caller asking for "too much history" is a harmless request to shrink, unlike a caller asking
    /// for an out-of-bounds TTL (<see cref="Post"/>'s own bounds check), which names a genuine
    /// contract violation. No SpectatorMode gate (mirrors <see cref="AnnouncementNowPlayingController.NowPlaying"/>'s
    /// own remarks): this route is never reachable by a public caller in the first place.
    /// </para>
    ///
    /// <para>
    /// <b>Reachable by the announce token, not just the admin session (T344 review finding F7 —
    /// documentation only, the surface stays exactly as wide as it is today).</b> This action carries
    /// no scheme list of its own; it inherits the class-level
    /// <see cref="AnnounceTokenAuthenticationDefaults.InScopeSchemes"/> authorization, so a caller
    /// holding only the F145.3 announce token can read the FULL history — every submitter's rows, not
    /// only its own. This is deliberate, not an oversight: SPEC F145.3 grants the token the whole
    /// announcements FAMILY (mint excepted — see <see cref="AnnouncementTokenController"/>'s own
    /// remarks), and STORY-361/PLAN T344 never asked for a narrower, per-submitter read. Least-
    /// privilege narrowing this route to session-only (or to the calling submitter's own rows) is a
    /// flagged revisit for a later cycle — the T346 era, with its own SPEC rider — not a gap to close
    /// silently here.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] int? limit, CancellationToken ct)
    {
        var cappedLimit = limit is { } requested and > 0 ? Math.Min(requested, MaxHistoryLimit) : DefaultHistoryLimit;

        var rows = await announcementStore.HistoryAsync(cappedLimit, ct);
        return Ok(rows.Select(ToHistoryDto).ToArray());
    }

    static AnnouncementHistoryDto ToHistoryDto(AnnouncementHistoryEntry entry) => new(
        entry.Id, entry.Message, entry.Verbatim, entry.State, entry.DeclineReason, entry.CollapseCount,
        entry.CreatedAt, entry.ExpiresAt, entry.AiredAt);

    static ProblemDetails SpectatorModeProblem() => new()
    {
        Status = StatusCodes.Status403Forbidden,
        Title  = "Announcements are disabled.",
        Detail = "The station is public (Station:SpectatorMode is on) — a public stream never carries the house's events (SPEC F145.1).",
    };

    static ProblemDetails BlankMessageProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid message.",
        Detail = "message must not be blank.",
    };

    static ProblemDetails MessageTooLongProblem(int maxChars) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid message.",
        Detail = $"message must be at most {maxChars} characters.",
    };

    static ProblemDetails VoiceTooLongProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid voice.",
        Detail = $"voice must be at most {MaxVoiceChars} characters.",
    };

    static ProblemDetails TtlOutOfBoundsProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid ttlSeconds.",
        Detail = $"ttlSeconds must be between {MinTtlSeconds} and {MaxTtlSeconds}, or omitted for the default.",
    };

    static ProblemDetails DepthCapProblem(int depthCap) => new()
    {
        Status = StatusCodes.Status429TooManyRequests,
        Title  = "Too many pending announcements.",
        Detail = $"At most {depthCap} announcements may be pending at once; wait for one to be delivered or to expire.",
    };

    static ProblemDetails AcceptedRateCapProblem(int acceptedPerMinute) => new()
    {
        Status = StatusCodes.Status429TooManyRequests,
        Title  = "Too many announcements accepted.",
        Detail = $"At most {acceptedPerMinute} announcements may be accepted per minute; wait for the window to roll over.",
    };

    // T339 review finding F3 — a distinct reason from MessageTooLongProblem: this names the STORE's
    // own hard limit (StoreMessageMaxChars, the DDL's fixed 280), not settings.MessageMaxChars, so
    // the caller always hears the cap that actually declined the write.
    static ProblemDetails StoreMessageCapProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid message.",
        Detail = $"message must be at most {StoreMessageMaxChars} characters (the store's own hard limit).",
    };
}
