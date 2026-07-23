using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The booth log's admin-only paged feed (SPEC F72.2, STORY-195) — "what did it say at 9:14" is
/// answerable from this endpoint alone. Never on any spectator/public surface (F72.4): no
/// <see cref="SpectatorSurfaceAttribute"/>, deny-by-default like every other admin route.
///
/// Also serves the taste-thumb accrual endpoint (SPEC F84.1, F84.5, F84.6; STORY-215, PLAN T70):
/// <c>POST /api/booth-log/{id}/taste-thumb</c>. One route shape covers BOTH the now-playing and
/// booth-log admin surfaces — the credited persona is whichever one is stamped on the booth-log row
/// itself (F84.1), never whichever persona happens to be active now, so a now-playing thumb is
/// simply "resolve to the latest track-start booth-log row, then call this same route" (T71's job;
/// no second endpoint shape exists for it to diverge from this one).
/// </summary>
[ApiController]
[Route("api/booth-log")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.PlayoutRead)]
public sealed class BoothLogController(
    IBoothLogReader store,
    IPersonaTasteAccrualStore accrual,
    IMediaLibraryMembership membership,
    ISafeScopeProvider safeScope,
    ILogger<BoothLogController> logger) : ControllerBase
{
    const int DefaultTake = 50;
    const int MaxTake = 200;

    /// <summary>
    /// GET /api/booth-log?before=&amp;take= — newest-first keyset page (SPEC F72.2). <c>before</c> is
    /// the opaque cursor from a previous response's <c>nextBefore</c> (absent = the newest page);
    /// <c>take</c> is clamped to [1, 200] (default 50). 400 for a malformed <c>before</c>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? before, [FromQuery] int? take, CancellationToken ct)
    {
        BoothLogCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(before) && !BoothLogCursor.TryParse(before, out cursor))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Validation error.",
                Detail = "before is not a valid cursor.",
            });
        }

        var effectiveTake = take is null or <= 0 ? DefaultTake : Math.Min(take.Value, MaxTake);

        var page = await store.ReadAsync(cursor, effectiveTake, ct);

        // gh-#99 — one batch membership resolve per page: which stamped media ids are safe-scope
        // content. Rows flagged here render no taste thumbs; ThumbTaste refuses them independently.
        var stampedIds = page.Entries.Select(e => e.MediaId).OfType<long>().ToList();
        var safeContentIds = await membership.FilterToLibrariesAsync(stampedIds, safeScope.Current, ct);

        return Ok(new BoothLogPageDto(
            page.Entries.Select(e => new BoothLogEntryDto(
                e.Id, e.OccurredAt, e.Kind, e.Summary, e.PersonaId, ToPickDto(e.Id, e.Pick),
                TasteExcluded: e.MediaId is long mediaId && safeContentIds.Contains(mediaId))).ToList(),
            page.NextBefore?.ToString()));
    }

    /// <summary>
    /// <paramref name="pick"/> is the row's raw <c>booth_log.pick</c> jsonb text (or
    /// <see langword="null"/>, SPEC F86.1) — deserialized through the one canonical
    /// <see cref="BoothLogPickStampSerializer"/> and narrowed to this endpoint's wire shape
    /// (F86.2). <see langword="null"/> in, <see langword="null"/> out: <see cref="BoothLogEntryDto.Pick"/>'s
    /// own <c>JsonIgnore(WhenWritingNull)</c> is what turns that into an ABSENT field on the wire.
    ///
    /// F72.2 (a working feed) takes priority over F86.1 (a decorative field): a stored
    /// <paramref name="pick"/> that is off-schema JSON (e.g. <c>{}</c> — every property missing, so
    /// <c>FiredRules</c> deserializes to <see langword="null"/> despite the record's own non-nullable
    /// annotation, since JSON deserialization fills constructor parameters by reflection, not through
    /// the record's own constructor) or not even valid JSON (<see cref="JsonException"/>) never 500s
    /// the whole page over one bad row — it degrades to "no pick chips" for that row, with ONE warning
    /// logged (row id included) so the corruption stays discoverable.
    /// </summary>
    BoothLogPickDto? ToPickDto(long rowId, string? pick)
    {
        if (pick is null)
            return null;

        BoothLogPickStamp? stamp;
        try
        {
            stamp = BoothLogPickStampSerializer.Deserialize(pick);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Booth-log row {RowId} has a pick that failed to deserialize — omitting it from the response", rowId);
            return null;
        }

        if (stamp?.FiredRules is null)
        {
            logger.LogWarning("Booth-log row {RowId} has an off-schema pick stamp — omitting it from the response", rowId);
            return null;
        }

        return new BoothLogPickDto(
            stamp.FiredRules.Select(rule => new BoothLogFiredRuleDto(rule.Summary, rule.Weight)).ToList(),
            stamp.IsExploration);
    }

    /// <summary>
    /// POST /api/booth-log/{id}/taste-thumb — nudge the accrued artist rule for whichever persona was
    /// stamped on booth-log row <paramref name="id"/> at air time (SPEC F84.1, F84.6). Body:
    /// <c>{ "direction": "up" | "down" }</c> (case-insensitive, mirrors <see cref="RatingController.Vote"/>'s
    /// own parsing). Invalid direction → 400, nothing written. Unknown row id → 404. A row with no
    /// persona stamp, not a track-start row, or no known artist → 400 (F84.6, not thumbable). A
    /// repeat thumb for the same (persona, row, direction) → 200, idempotent no-op (F84.5).
    /// </summary>
    [HttpPost("{id:long}/taste-thumb")]
    [Consumes("application/json")]
    // gh-#8: the one WRITE on this otherwise read-only surface. [Authorize] attributes COMPOSE
    // (AND): a thumb needs the class's PlayoutRead AND Curation — deliberately, since thumbing
    // from the booth log means seeing the log and shaping taste. Identical gate today (both map
    // to AdminOnlyRequirement); the distinction only bites once an RBAC module differentiates.
    [Authorize(Policy = AuthorizationPolicies.Curation)]
    public async Task<IActionResult> ThumbTaste(long id, [FromBody] TasteThumbRequest request, CancellationToken ct)
    {
        if (!TryParseDirection(request.Direction, out var direction))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid direction.",
                Detail = "direction must be \"up\" or \"down\".",
            });
        }

        // gh-#99 — safe-scope content never accrues taste: a safe-loop track or station ID airing
        // would teach the persona an artist rule for the STATION's own name. Resolved here, on the
        // library connection, because the accrual store's transaction runs as station_svc, which
        // deliberately cannot join library.media. The row being immutable makes this two-step safe.
        if (await store.GetMediaIdAsync(id, ct) is long mediaId)
        {
            var safeContent = await membership.FilterToLibrariesAsync([mediaId], safeScope.Current, ct);
            if (safeContent.Contains(mediaId))
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title  = "Not thumbable.",
                    Detail = $"Booth-log row {id} is safe-loop/station-ID content (Station:SafeScope:LibraryIds) — taste thumbs do not apply (gh-#99).",
                });
            }
        }

        var outcome = await accrual.ThumbAsync(id, direction, ct);

        return outcome switch
        {
            TasteThumbOutcome.Nudged nudged => Ok(new TasteThumbResponse(AlreadyRecorded: false, nudged.Weight)),
            TasteThumbOutcome.AlreadyRecorded => Ok(new TasteThumbResponse(AlreadyRecorded: true, Weight: null)),
            TasteThumbOutcome.RowNotFound => NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title  = "Not found.",
                Detail = $"No booth-log row with id {id} exists.",
            }),
            TasteThumbOutcome.NotThumbable => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Not thumbable.",
                Detail = $"Booth-log row {id} has no persona stamp, is not a track-start row, or has no known artist (F84.6).",
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>Case-insensitive match against the only two valid direction values — mirrors <c>RatingController</c>'s own.</summary>
    static bool TryParseDirection(string? direction, out TasteThumbDirection parsed)
    {
        switch (direction?.Trim().ToLowerInvariant())
        {
            case "up":
                parsed = TasteThumbDirection.Up;
                return true;
            case "down":
                parsed = TasteThumbDirection.Down;
                return true;
            default:
                parsed = default;
                return false;
        }
    }
}
