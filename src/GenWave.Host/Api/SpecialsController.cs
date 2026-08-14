using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The dated-specials tail's own CRUD (SPEC F120.1, F120.3; STORY-317, PLAN T259):
/// <c>GET/POST/DELETE /api/schedule/specials</c> over <see cref="IScheduleSpecialStore"/> — this
/// store's first Host call site (<see cref="IScheduleSpecialStore"/>'s own remarks name PLAN T259 as
/// exactly that). List + create + delete only, mirroring the store's own deliberately minimal shape;
/// there is no PATCH/PUT — an operator wanting to change a special deletes and re-creates it (SPEC
/// F120.3's own "edit = delete+recreate is acceptable for v1" allowance; the Admin UI form makes that
/// exact round trip on an "edit").
///
/// <para>
/// <b>The resolver now consumes this live (T118→T120→T260 pattern, completed here).</b> Exactly like
/// <see cref="IScheduleStore"/> reached T120 and <see cref="IShowStore"/> reached T240: this
/// controller already made the store LIVE for authoring (PLAN T259 — an operator can author/list/
/// remove a special through the Admin UI); PLAN T260 is what makes a written special actually shadow
/// the weekly grid on the production feeder tick — <c>GenWave.Orchestration.CachingScheduleResolver</c>
/// reads <see cref="IScheduleSpecialStore"/> itself now (alongside <see cref="IScheduleStore"/>) and
/// hands the result into <c>ScheduleResolver.Resolve</c>'s own specials-first rung (SPEC F120.2, PLAN
/// T258) on every resolve. A special created through this controller is on the air within one cache
/// cycle — see <c>CachingScheduleResolver</c>'s own remarks for the caching/invalidation design.
/// </para>
///
/// <para>
/// <b>App-side validation gate order (SPEC F120.1's own "F91 constraints mirrored" instruction,
/// applied here the same way <c>ScheduleController.Put</c> validates a submitted week BEFORE any
/// statement touches the database).</b> <see cref="IScheduleSpecialStore.CreateAsync"/>'s own remarks
/// are explicit that it runs NO app-side PRE-validation — this action is where every check has to run
/// FIRST, "reject BEFORE the DB" (mirrors <c>ShowRepository.ValidateName</c>/<c>ValidateBudgets</c>'s
/// own "pure C#, before either write method ever opens a connection" posture, translated to the
/// endpoint layer here because the store's PRE-write validation is deliberately minimal): 30-minute
/// step/range on both minutes (<c>station.segment_schedule</c>'s own CHECKs, mirrored verbatim per
/// db/36's header), end&gt;start, the date-not-in-the-past product rule (see <see cref="Create"/>'s own
/// remarks), then persona/show EXISTENCE (fail-closed 400 naming which one is unknown —
/// <c>ScheduleController.AssignShow</c>'s own "an unknown id referenced by the request BODY is a 400,
/// not a 404" precedent). Only once every one of those passes does this action ever call
/// <see cref="IScheduleSpecialStore.CreateAsync"/> — the two remaining rejections db/36 can still raise
/// past that point (a same-date overlap via the EXCLUDE constraint, or a persona/show deleted in the
/// race window between the check above and the insert) arrive back as a typed
/// <see cref="ScheduleSpecialCreateResult"/> case, never a raw <c>Npgsql.PostgresException</c> this
/// controller would have to catch itself — <see cref="ScheduleSpecialCreateResult"/>'s own remarks
/// explain why that translation lives in the repository layer instead (the L2 architecture law this
/// project's own fitness suite enforces).
/// </para>
///
/// <para>
/// <b>The Shows delete guard names a referencing special; so does the Persona one now (gh-#462).</b>
/// <c>ShowsController.Delete</c> re-queries <see cref="IScheduleSpecialStore.ListUpcomingAsync"/> and
/// filters by show id to NAME a referencing special in its 409 — cheap, because
/// <c>ScheduleSpecial.ShowId</c> is already the exact shape <c>ScheduledSlot</c> (the weekly-block
/// guard's own payload type) can't carry anyway, so there was no existing type to reconcile.
/// <c>PersonaController.Delete</c>'s guard took a different route, once it landed (gh-#462): it needed
/// to represent a DATE, not just a day-of-week/minute span, so <c>PersonaWriteResult.ScheduledElsewhere</c>
/// grew a second payload, <c>Specials</c> — a narrow <see cref="Core.Domain.ScheduledSpecialSlot"/>
/// projection (<see cref="Core.Domain.ScheduledSlot"/>'s own sibling), never this store's full
/// <see cref="Core.Domain.ScheduleSpecial"/> row — populated by <c>PersonaRepository.DeleteAsync</c>'s
/// own direct pre-query of <c>station.schedule_special</c> (never through
/// <see cref="IScheduleSpecialStore"/> at all, unlike the Shows guard above), since that store's
/// <see cref="IScheduleSpecialStore.ListUpcomingAsync"/> is scoped to upcoming rows only and this guard
/// needs every blocking row regardless of date.
/// </para>
///
/// Security: <c>AdminSurface</c>/<c>Settings</c> — the same admin-plane pairing every other
/// station-configuration controller in this file's directory carries
/// (<see cref="ScheduleController"/>/<see cref="ShowsController"/>/<see cref="PersonaController"/>).
/// <see cref="Create"/> requires <c>Content-Type: application/json</c> (415 otherwise, F18.7).
/// </summary>
[ApiController]
[Route("api/schedule/specials")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class SpecialsController(
    IScheduleSpecialStore specialStore,
    IPersonaStore personaStore,
    IShowStore showStore,
    IStationClockProvider stationClock,
    ILogger<SpecialsController> logger) : ControllerBase
{
    /// <summary>
    /// GET /api/schedule/specials — every special on or after the station's own "today" (SPEC F120.1's
    /// own "admin list view showing every special from today forward" framing;
    /// <see cref="IScheduleSpecialStore.ListUpcomingAsync"/>'s own remarks explain why this action
    /// supplies "today" rather than the store reading a clock itself), ordered by date then start
    /// minute.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var specials = await specialStore.ListUpcomingAsync(stationClock.Today(), ct);
        return Ok(specials.Select(ToDto).ToArray());
    }

    /// <summary>
    /// POST /api/schedule/specials — create a dated special. 201 with the row on success; 400 for an
    /// off-grid/inverted minute range, a past date, or an unknown persona/show id; 409 when
    /// <see cref="IScheduleSpecialStore.CreateAsync"/> answers <see cref="ScheduleSpecialCreateResult.Overlap"/>
    /// (the span overlaps another special on the SAME date, db/36's per-date EXCLUDE, SPEC F120.1) or
    /// <see cref="ScheduleSpecialCreateResult.UnknownReference"/> (the named persona/show was deleted in
    /// the race window between this action's own existence check and the insert — mirrors
    /// <c>ScheduleController.Put</c>'s own documented persona-race 409; see this class's own remarks for
    /// the full gate order).
    ///
    /// <para>
    /// <b>Date-not-in-the-past (product call, PLAN T259).</b> TODAY is allowed — an operator authoring
    /// a special for "tonight" before the resolver would even reach that span is a legitimate same-day
    /// edit — only a date that has already fully elapsed (yesterday or earlier, compared against the
    /// station's own local calendar date via <see cref="IStationClockProvider"/>, never the
    /// container's) is refused. This is deliberately a CALENDAR-date comparison, not a
    /// date-plus-minute one: a special dated today whose span already elapsed by wall-clock time is
    /// still accepted — SPEC F120 gives no "already passed today" rule, and inventing one here would be
    /// scope this task was never asked to cover.
    /// </para>
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] SpecialRequestDto request, CancellationToken ct)
    {
        if (request.StartMinute % 30 != 0 || request.StartMinute is < 0 or > 1410)
            return BadRequest(InvalidRangeProblem(
                $"startMinute {request.StartMinute} must be a multiple of 30 within [0, 1410]."));

        if (request.EndMinute % 30 != 0 || request.EndMinute is < 30 or > 1440)
            return BadRequest(InvalidRangeProblem(
                $"endMinute {request.EndMinute} must be a multiple of 30 within [30, 1440]."));

        if (request.EndMinute <= request.StartMinute)
            return BadRequest(InvalidRangeProblem(
                $"endMinute {request.EndMinute} must be greater than startMinute {request.StartMinute}."));

        var today = stationClock.Today();
        if (request.OnDate < today)
            return BadRequest(PastDateProblem(request.OnDate, today));

        if (request.PersonaId is { } personaId && await personaStore.GetByIdAsync(personaId, ct) is null)
            return BadRequest(UnknownPersonaProblem(personaId));

        if (request.ShowId is { } showId && await showStore.GetByIdAsync(showId, ct) is null)
            return BadRequest(UnknownShowProblem(showId));

        var draft = new ScheduleSpecial(
            Id: null, request.OnDate, request.StartMinute, request.EndMinute, request.PersonaId,
            request.Genres, request.EnergyMin, request.EnergyMax, Show: null, ShowId: request.ShowId);

        var result = await specialStore.CreateAsync(draft, ct);

        switch (result)
        {
            case ScheduleSpecialCreateResult.Created created:
                // Invariant-rendered date (the same posture PastDateProblem/OverlapProblem took at the
                // T259 review): DateOnly's default ToString is ambient-culture, and rendering it
                // explicitly also makes the "value" provably fixed-format for log-analysis tooling.
                logger.LogInformation(
                    "Special created id={SpecialId} onDate={OnDate} startMinute={StartMinute} endMinute={EndMinute}",
                    created.Special.Id,
                    created.Special.OnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    created.Special.StartMinute, created.Special.EndMinute);
                return StatusCode(StatusCodes.Status201Created, ToDto(created.Special));

            case ScheduleSpecialCreateResult.Overlap:
                logger.LogWarning(
                    "Special create refused: overlaps another special onDate={OnDate}",
                    request.OnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return Conflict(OverlapProblem(request.OnDate));

            case ScheduleSpecialCreateResult.UnknownReference:
                // Race backstop, not the primary signal (mirrors ScheduleController.Put's own
                // remarks): the persona/show existed at the GetByIdAsync checks above but was
                // deleted before this insert committed.
                logger.LogWarning("Special create raced a concurrent persona/show delete");
                return Conflict(RaceConflictProblem());

            default:
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>DELETE /api/schedule/specials/{id} — 204 on success, 404 for an unknown id.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var deleted = await specialStore.DeleteAsync(id, ct);
        if (deleted)
            logger.LogInformation("Special deleted id={SpecialId}", id);

        return deleted ? NoContent() : NotFound(NotFoundProblem(id));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static SpecialDto ToDto(ScheduleSpecial special) => new(
        special.Id ?? throw new UnreachableException("A persisted special always carries a store-assigned id."),
        special.OnDate, special.StartMinute, special.EndMinute, special.PersonaId,
        special.Genres, special.EnergyMin, special.EnergyMax, special.ShowId);

    static ProblemDetails InvalidRangeProblem(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid time range.",
        Detail = detail,
    };

    static ProblemDetails PastDateProblem(DateOnly onDate, DateOnly today) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid date.",
        Detail =
            $"onDate {onDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is in the past; " +
            $"the earliest allowed date is today ({today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}).",
    };

    static ProblemDetails UnknownPersonaProblem(long personaId) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown persona.",
        Detail = $"No persona with id {personaId} exists.",
    };

    static ProblemDetails UnknownShowProblem(long showId) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown show.",
        Detail = $"No show with id {showId} exists.",
    };

    static ProblemDetails OverlapProblem(DateOnly onDate) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Special overlaps another.",
        Detail = $"Another special already covers this time range on {onDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
    };

    static ProblemDetails RaceConflictProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Special create conflicted with a concurrent change.",
        Detail = "The persona or show referenced by this submission changed while it was being saved. Reload and try again.",
    };

    static ProblemDetails NotFoundProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No special with id {id} exists.",
    };
}
