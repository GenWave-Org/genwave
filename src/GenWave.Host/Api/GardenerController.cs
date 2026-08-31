using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The Library Gardener's own admin surface (SPEC F153.9; STORY-374; PLAN T377, gh-#529) —
/// <c>GET /api/gardener/findings</c> lists the queue grouped by <see cref="RotKind"/>, with
/// <see cref="RotKind.NearDuplicate"/> rows additionally grouped by their own <c>group_key</c>;
/// <c>POST /api/gardener/findings/{id}/dismiss</c> is the store-level dismiss's own HTTP door
/// (<see cref="IRotFindingStore.DismissAsync"/> already does the actual write, T372 review MED-3).
/// Same <see cref="AdminSurfaceAttribute"/> pairing every admin controller carries, gated to
/// <see cref="AuthorizationPolicies.Curation"/> — shaping the library and its taste signals is
/// exactly the plane this queue lives on (media, ratings, re-enrichment already sit behind the same
/// policy name).
///
/// <para>
/// <b>Kind/state wire text (T377 review BLOCKING).</b> Neither this controller nor
/// <c>Garden.RotFindingRepository</c> hand-roll a <see cref="RotKind"/>/<see cref="RotState"/> ↔
/// snake_case switch of their own any more — both call through <see cref="RotKindTokens"/>/
/// <see cref="RotStateTokens"/>, the ONE map each enum gets (mirrors <see cref="ImagingKindTokens"/>).
/// Five independent copies used to exist across these two files; a kind added to the enum but missed
/// in even one of them would compile fine and 500 out of <see cref="BuildGroup"/> the first time a
/// row of that kind was listed. A single shared map does not turn that into a compile error either
/// (<see cref="RotKindTokens.ToToken"/> keeps a discard arm) — but a miss now surfaces immediately,
/// at RUNTIME, the moment <see cref="RotKindTokens.Tokens"/>'s own static initializer first runs
/// (a <see cref="TypeInitializationException"/>), and the Core round-trip fact over every enum value
/// goes red the same way, rather than waiting for a specific kind to reach this endpoint.
/// </para>
/// </summary>
[ApiController]
[Route("api/gardener")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class GardenerController(IRotFindingStore store, ILogger<GardenerController> logger) : ControllerBase
{
    /// <summary>Endpoint default when <c>limit</c> is omitted — matches
    /// <see cref="IRotFindingStore.ListAsync"/>'s own default.</summary>
    const int DefaultLimit = 200;

    /// <summary>Endpoint ceiling — matches <c>Garden.RotFindingRepository.ClampPaging</c>'s own cap,
    /// the T372 LOW-2 figure. Kept in sync by convention (both floors are independently enforced,
    /// T377 review): the repository's own floor is the one that actually holds regardless of this
    /// value ever drifting.</summary>
    const int MaxLimit = 1000;

    /// <summary>
    /// GET /api/gardener/findings?kind=&amp;state=&amp;limit=&amp;offset= (SPEC F153.9, STORY-374
    /// AC7/AC10) — 200 with <c>{ groups: [ { kind, findings: [...], duplicateGroups: [...] } ] }</c>.
    /// Every group carries <c>findings</c> — a flat row per finding (<c>id</c>, <c>mediaId</c>,
    /// <c>state</c>, <c>evidence</c> as a parsed JSON object — never a re-stringified blob,
    /// <c>openedAt</c>, <c>resolvedAt</c>, <c>dismissedAt</c>, and a nested <c>media</c> object —
    /// <c>path</c>, <c>title</c>, <c>artist</c>, <c>durationMs</c>, <c>plays</c>, <c>rating</c>,
    /// <c>neverPlay</c>, <c>eligible</c>, sourced entirely from
    /// <see cref="IRotFindingStore.ListWithMediaAsync"/>'s ONE joined read); ONLY a
    /// <see cref="RotKind.NearDuplicate"/> group's <c>duplicateGroups</c> is non-empty — the SAME row
    /// objects already in <c>findings</c> (never re-projected, T377 review LOW-3), re-grouped by
    /// <c>groupKey</c> into <c>{ groupKey, members }</c> so a caller can render "these N rows are one
    /// duplicate cluster" without re-deriving the grouping client-side. Every OTHER kind's
    /// <c>duplicateGroups</c> is present but empty — one predictable shape for every group, never a
    /// property that sometimes isn't there.
    ///
    /// <para>
    /// <b>No <c>count</c> field, no <c>X-Pagination</c> header (T377 review MED, RULED).</b> This
    /// queue is not a browse table: rows are paged FLAT, in the SAME
    /// <c>kind, group_key nulls last, opened_at desc, id</c> order
    /// <see cref="IRotFindingStore.ListWithMediaAsync"/>'s own remarks describe, BEFORE this action
    /// ever groups them by kind — a group's own rows are adjacent within a page, but a page boundary
    /// can still fall inside a group once the queue is large enough to page at all. A per-group
    /// <c>count</c> would therefore be the count within THIS page only, not the kind's real total,
    /// and shipping it invites exactly that misreading. The real per-kind OPEN total lives on
    /// <c>GET /api/status</c>'s own <c>gardener.open</c> block (<see cref="IRotFindingStore.CountOpenByKindAsync"/>)
    /// — a caller wanting "how many near-duplicate findings exist" reads THAT, never this response's
    /// shape. A caller wanting the WHOLE queue in one page (e.g. a future review-queue UI) passes
    /// this endpoint's own ceiling as <c>limit</c> instead of paging — see below for the exact
    /// default/ceiling values.
    /// </para>
    ///
    /// <c>kind</c>/<c>state</c> are the store's own snake_case wire text (<see cref="RotKindTokens"/>/
    /// <see cref="RotStateTokens"/>: <c>dead_file</c>, <c>near_duplicate</c>, <c>stale_metadata</c>,
    /// <c>shelf_dust</c>, <c>unreachable</c> / <c>open</c>, <c>dismissed</c>, <c>resolved</c>) — the
    /// same lowercase-with-underscores token this codebase already surfaces enum-shaped values as
    /// elsewhere (e.g. <c>AdminMediaDto.ImagingKind</c>'s <c>station_id</c>/<c>jingle</c>/<c>promo</c>).
    /// Both are optional; omitted means "any" for that axis (F153.9: <c>state</c> defaults to every
    /// state, NOT just <c>open</c> — a caller wanting only the live queue passes <c>state=open</c>
    /// explicitly). An unrecognised value for either is a 400 <see cref="ProblemDetails"/> naming the
    /// field and the allowed set, never the caller's own value (log-forging/reflection posture this
    /// whole admin surface holds).
    ///
    /// <c>limit</c> defaults to <see cref="DefaultLimit"/>, clamped to [1, <see cref="MaxLimit"/>];
    /// <c>offset</c> clamped to ≥ 0 — SILENTLY, never a 400, mirroring <c>MediaController.List</c>'s
    /// own <c>Math.Clamp</c> posture for paging (a paging value is a hint, not a contract a client can
    /// get "wrong"). This is a courtesy shaping the response for a well-behaved caller;
    /// <c>Garden.RotFindingRepository</c>'s own floor is what actually enforces the bound (T372
    /// review LOW-2, T377 review) regardless of what reaches it here.
    /// </summary>
    [HttpGet("findings")]
    public async Task<IActionResult> GetFindings(
        [FromQuery] string? kind,
        [FromQuery] string? state,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        RotKind? kindFilter = null;
        if (kind is not null)
        {
            if (!RotKindTokens.TryParse(kind, out var parsedKind))
                return BadRequest(InvalidQueryValueProblem("kind", RotKindTokens.Tokens));

            kindFilter = parsedKind;
        }

        RotState? stateFilter = null;
        if (state is not null)
        {
            if (!RotStateTokens.TryParse(state, out var parsedState))
                return BadRequest(InvalidQueryValueProblem("state", RotStateTokens.Tokens));

            stateFilter = parsedState;
        }

        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var effectiveOffset = Math.Max(offset ?? 0, 0);

        var page = await store.ListWithMediaAsync(kindFilter, stateFilter, effectiveLimit, effectiveOffset, ct);

        var groups = page.Items
            .GroupBy(row => row.Finding.Kind)
            .Select(BuildGroup)
            .ToList();

        return Ok(new { groups });
    }

    /// <summary>
    /// POST /api/gardener/findings/{id}/dismiss (SPEC F153.2; STORY-374 AC4) — the HTTP door onto
    /// <see cref="IRotFindingStore.DismissAsync"/>, which already carries the whole rule: an
    /// <see cref="RotState.Open"/> row moves to <see cref="RotState.Dismissed"/> and stays there
    /// forever, no pass ever re-opening it. 204 on success; 404 <see cref="ProblemDetails"/> (naming
    /// only the numeric <paramref name="id"/> — never free-text, so no log-forging/reflection concern)
    /// for every OTHER outcome the store's own contract already defines as a no-op returning
    /// <see langword="false"/> — an unknown id, a row already <see cref="RotState.Dismissed"/>, or
    /// one currently <see cref="RotState.Resolved"/>. This action adds no id-existence check of its
    /// own before calling the store — <see cref="IRotFindingStore.DismissAsync"/>'s single
    /// conditional <c>UPDATE</c> already answers both questions ("does it exist" and "was it open")
    /// in one round trip, so a second read here would only add a TOCTOU gap, not a real check.
    /// </summary>
    [HttpPost("findings/{id:long}/dismiss")]
    public async Task<IActionResult> Dismiss(long id, CancellationToken ct)
    {
        var dismissed = await store.DismissAsync(id, ct);
        if (!dismissed)
            return NotFound(NotFoundProblem(id));

        logger.LogInformation("Gardener finding dismissed id={FindingId}", id);
        return NoContent();
    }

    // ── Response shaping ────────────────────────────────────────────────────

    /// <summary>
    /// T377 review LOW-3: each row is projected through <see cref="BuildFindingDto"/> exactly ONCE
    /// (<paramref name="group"/>'s own rows paired with their already-built DTO) — <c>findings</c>
    /// and a <see cref="RotKind.NearDuplicate"/> group's own <c>duplicateGroups[].members</c> share
    /// the SAME DTO instances rather than each re-running the projection over the same rows.
    /// </summary>
    static object BuildGroup(IGrouping<RotKind, RotFindingWithMedia> group)
    {
        var projected = group.Select(row => (row.Finding.GroupKey, Dto: BuildFindingDto(row))).ToList();

        var duplicateGroups = (group.Key == RotKind.NearDuplicate
                ? projected.GroupBy(entry => entry.GroupKey)
                : Enumerable.Empty<IGrouping<string?, (string? GroupKey, object Dto)>>())
            .Select(duplicateGroup => new
            {
                groupKey = duplicateGroup.Key,
                members = duplicateGroup.Select(entry => entry.Dto).ToList(),
            })
            .ToList();

        return new
        {
            kind = RotKindTokens.ToToken(group.Key),
            findings = projected.Select(entry => entry.Dto).ToList(),
            duplicateGroups,
        };
    }

    static object BuildFindingDto(RotFindingWithMedia row) => new
    {
        id = row.Finding.Id,
        mediaId = row.Finding.MediaId,
        state = RotStateTokens.ToToken(row.Finding.State),
        evidence = JsonSerializer.Deserialize<JsonElement>(row.Finding.Evidence),
        openedAt = row.Finding.OpenedAt,
        resolvedAt = row.Finding.ResolvedAt,
        dismissedAt = row.Finding.DismissedAt,
        media = new
        {
            path = row.Locator,
            title = row.Title,
            artist = row.Artist,
            durationMs = row.DurationMs,
            plays = row.Plays,
            rating = row.Rating,
            neverPlay = row.NeverPlay,
            eligible = row.Eligible,
        },
    };

    // ── Problem builders ─────────────────────────────────────────────────────

    /// <summary>Names the offending FIELD and the allowed token set — never the caller's own value
    /// (log-forging/reflection posture, matches <c>ShowRotationController</c>/<c>MediaController</c>'s
    /// own 400 shape).</summary>
    static ProblemDetails InvalidQueryValueProblem(string field, IReadOnlyList<string> allowed) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = $"{field} must be one of: {string.Join(", ", allowed)}.",
    };

    /// <summary>A bare numeric id — never free text, so this carries no log-forging/reflection
    /// concern (mirrors <c>ShowRotationController.NotFoundProblemById</c>).</summary>
    static ProblemDetails NotFoundProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No open gardener finding with id {id} exists.",
    };
}
