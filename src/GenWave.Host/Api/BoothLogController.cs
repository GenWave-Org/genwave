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
///
/// And the station-level rotation-nudge sibling (SPEC F150.1, F150.8; STORY-370, PLAN T367):
/// <c>POST /api/booth-log/{id}/station-thumb</c>, sitting BESIDE <see cref="ThumbTaste"/> on the same
/// row with its own distinct glyph (T369, admin-ui) — the two never share a click, and never share a
/// write path: <see cref="ThumbStation"/> reaches <see cref="IThumbStore"/> only, never
/// <see cref="IPersonaTasteAccrualStore"/> or the F33 rating ledger (F155.3's disjointness pin,
/// GenWave.Architecture.Tests, proves this at the IL level for this exact action method).
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
    IThumbStore thumbStore,
    ILogger<BoothLogController> logger) : ControllerBase
{
    const int DefaultTake = 50;
    const int MaxTake = 200;

    /// <summary>SPEC F150.8's own row-kind gate — a station thumb applies to an aired track only.</summary>
    const string TrackStartedKind = "track-started";

    /// <summary>SPEC F150.7 — every operator thumb, from either surface (now-playing or booth log),
    /// carries this exact <c>listener_key</c>; idempotency for an operator thumb is therefore per
    /// (media, airing), the same triple a spectator's own hashed cookie key uses one field over.</summary>
    const string OperatorListenerKey = "operator";

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
            page.Entries.Select(e =>
            {
                var (pick, crosstalk) = ToPickOrCrosstalkDto(e.Id, e.Pick);
                return new BoothLogEntryDto(
                    e.Id, e.OccurredAt, e.Kind, e.Summary, e.PersonaId, pick,
                    TasteExcluded: e.MediaId is long mediaId && safeContentIds.Contains(mediaId),
                    Crosstalk: crosstalk);
            }).ToList(),
            page.NextBefore?.ToString()));
    }

    /// <summary>
    /// <paramref name="pick"/> is the row's raw <c>booth_log.pick</c> jsonb text (or
    /// <see langword="null"/>, SPEC F86.1) — dispatched to whichever of the TWO shapes that column can
    /// hold (<see cref="BoothLogEntryDto.Pick"/> for a persona pick, <see cref="BoothLogEntryDto.Crosstalk"/>
    /// for a <c>SegmentKind.Crosstalk</c> row's own two-voice script, SPEC F127.11, PLAN T287 — mutually
    /// exclusive by construction, <c>BoothLogWriter.BuildPickStamp</c>'s own remarks). <see langword="null"/>
    /// in, both <see langword="null"/> out: each DTO's own <c>JsonIgnore(WhenWritingNull)</c> is what
    /// turns that into an ABSENT field on the wire.
    ///
    /// <para>
    /// <b>Crosstalk tried FIRST (review finding F3 — narrow fix over the pre-fix defect).</b>
    /// <see cref="CrosstalkAiredScriptSerializer.Deserialize"/> is now validated (round-2 review F9 —
    /// the sibling serializer's own documented off-schema trap): it returns <see langword="null"/> for
    /// anything that is not genuinely a <c>{"lines":[...]}</c> shape, so trying it first here never
    /// misclassifies an ordinary persona-pick stamp as crosstalk — only a row whose <c>pick</c> IS a
    /// crosstalk script ever takes this branch, and it does so with no WARN at all (this is the
    /// everyday, valid shape for that row's own kind, not corruption). Every other row falls through to
    /// the persona-pick path exactly as before.
    /// </para>
    ///
    /// <para>
    /// F72.2 (a working feed) takes priority over F86.1 (a decorative field): a stored
    /// <paramref name="pick"/> that is off-schema JSON for BOTH shapes (e.g. <c>{}</c> — every property
    /// missing, so <c>BoothLogPickStamp.FiredRules</c> deserializes to <see langword="null"/> despite
    /// the record's own non-nullable annotation, since JSON deserialization fills constructor
    /// parameters by reflection, not through the record's own constructor) or not even valid JSON
    /// (<see cref="JsonException"/>) never 500s the whole page over one bad row — it degrades to "no
    /// pick chips" for that row, with ONE warning logged (row id included) so the corruption stays
    /// discoverable.
    /// </para>
    /// </summary>
    (BoothLogPickDto? Pick, BoothLogCrosstalkScriptDto? Crosstalk) ToPickOrCrosstalkDto(long rowId, string? pick)
    {
        if (pick is null)
            return (null, null);

        try
        {
            if (CrosstalkAiredScriptSerializer.Deserialize(pick) is { } script)
            {
                return (null, new BoothLogCrosstalkScriptDto(
                    script.Lines
                        .Select(line => new BoothLogCrosstalkLineDto(line.Speaker.ToString(), line.Text, line.IsInterjection))
                        .ToList()));
            }

            if (BoothLogPickStampSerializer.Deserialize(pick) is { FiredRules: { } firedRules } stamp)
            {
                return (new BoothLogPickDto(
                    firedRules.Select(rule => new BoothLogFiredRuleDto(rule.Summary, rule.Weight)).ToList(),
                    stamp.IsExploration), null);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Booth-log row {RowId} has a pick that failed to deserialize — omitting it from the response", rowId);
            return (null, null);
        }

        logger.LogWarning("Booth-log row {RowId} has an off-schema pick stamp — omitting it from the response", rowId);
        return (null, null);
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

    /// <summary>
    /// POST /api/booth-log/{id}/station-thumb — the station-level rotation-nudge sibling of
    /// <see cref="ThumbTaste"/> (SPEC F150.1, F150.7, F150.8; STORY-370, PLAN T367). Body:
    /// <c>{ "direction": "up" | "down" }</c>. Unknown row id → 404. A row that is not a
    /// <c>"track-started"</c> row, or carries no stamped catalog media id, → 400 NAMING the row's own
    /// kind (F150.8) — a station thumb only ever applies to a specific aired track.
    ///
    /// <para>
    /// <b>The airing key (T367 review MED-4, corrected).</b> <see cref="IThumbStore.RecordAsync"/> is
    /// keyed <c>(media_id, airing_started_at, listener_key)</c> — this action uses row
    /// <paramref name="id"/>'s OWN <c>occurred_at</c> as <c>airing_started_at</c>. That is
    /// DELIBERATELY NOT the same instant a listener's own airing token keys against
    /// (<c>AiringTokenRing</c>'s <c>StartedAt</c>, stamped synchronously off <c>TrackAired</c>):
    /// <c>BoothLogRepository</c>'s own insert omits <c>occurred_at</c> from its column list entirely,
    /// so the row takes the column's <c>DEFAULT now()</c> — the Postgres SERVER's own wall clock at
    /// DRAIN time (whenever the booth-log queue actually flushes this row), not the ring's own
    /// earlier, in-process stamp. An operator thumb (keyed off this drain-time DB stamp) and a
    /// listener thumb on the SAME physical airing (keyed off the ring's own, earlier `StartedAt`)
    /// therefore land as TWO DIFFERENT <c>library.media_thumb</c> rows — different
    /// <c>airing_started_at</c> values, and in any case different <c>listener_key</c>s
    /// (<c>"operator"</c> vs. a spectator's own hashed cookie key, F150.7) — never the SAME row
    /// merged across sources. This is harmless: <c>library.recompute_nudge</c> aggregates every
    /// <c>media_thumb</c> row for a <c>media_id</c> regardless of which <c>airing_started_at</c> it
    /// carries (<c>MediaThumbRepository</c>'s own remarks), so both rows count toward the SAME
    /// track's <c>nudge</c> either way — the split key changes accounting granularity, never
    /// correctness.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="ThumbWriteResult.Ignored"/> is still 200 (SPEC F150.1, F150.8).</b> Unlike the
    /// public spectator surface's no-oracle constant-202 (F150.3 — a public-surface rule only), the
    /// operator MAY be told a thumb landed on safe-scope/unknown content: every
    /// <see cref="ThumbWriteResult"/> outcome answers 200 with its OWN <see cref="StationThumbResponse.Result"/>
    /// token, never collapsed to one shared body.
    /// </para>
    ///
    /// <para>
    /// <b>Disjointness (SPEC F150.1, F155.3).</b> This method reaches <see cref="IThumbStore"/> only —
    /// never <see cref="IPersonaTasteAccrualStore"/>, never any type writing <c>library.media_rating</c>
    /// or <c>station.persona_taste</c>. GenWave.Architecture.Tests' three-way disjointness pin proves
    /// this by walking the real IL call graph from this exact action; do not add a call here to
    /// either without reading that fact's own remarks first.
    /// </para>
    /// </summary>
    [HttpPost("{id:long}/station-thumb")]
    [Consumes("application/json")]
    [Authorize(Policy = AuthorizationPolicies.Curation)]
    public async Task<IActionResult> ThumbStation(long id, [FromBody] StationThumbRequest request, CancellationToken ct)
    {
        if (!TryParseThumbDirection(request.Direction, out var direction))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid direction.",
                Detail = "direction must be \"up\" or \"down\".",
            });
        }

        var airing = await store.GetTrackAiringAsync(id, ct);
        if (airing is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title  = "Not found.",
                Detail = $"No booth-log row with id {id} exists.",
            });
        }

        if (airing.Kind != TrackStartedKind || airing.MediaId is not long mediaId)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Not thumbable.",
                Detail = $"Booth-log row {id} is a \"{airing.Kind}\" row, not a track airing — station thumbs apply to aired tracks only (F150.8).",
            });
        }

        var result = await thumbStore.RecordAsync(
            mediaId, airing.OccurredAt, OperatorListenerKey, direction, ThumbSource.Operator, ct);

        return Ok(new StationThumbResponse(ToResultText(result)));
    }

    /// <summary>Case-insensitive match against the only two valid direction values — the
    /// <see cref="ThumbDirection"/>-typed sibling of <see cref="TryParseDirection"/>, kept as its own
    /// method rather than a shared helper: the two enums are deliberately distinct types
    /// (<see cref="StationThumbRequest"/>'s own remarks).</summary>
    static bool TryParseThumbDirection(string? direction, out ThumbDirection parsed)
    {
        switch (direction?.Trim().ToLowerInvariant())
        {
            case "up":
                parsed = ThumbDirection.Up;
                return true;
            case "down":
                parsed = ThumbDirection.Down;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    static string ToResultText(ThumbWriteResult result) => result switch
    {
        ThumbWriteResult.Recorded => "recorded",
        ThumbWriteResult.Unchanged => "unchanged",
        ThumbWriteResult.Flipped => "flipped",
        ThumbWriteResult.Ignored => "ignored",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unmapped ThumbWriteResult."),
    };
}
