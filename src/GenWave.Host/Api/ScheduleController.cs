using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The format-clock week document (SPEC F91.1, F91.8; STORY-240, PLAN T122): <c>GET /api/schedule</c>
/// reads the whole grid, <c>PUT /api/schedule</c> atomically replaces it, both over
/// <see cref="IScheduleStore"/>. The T129 drag-paint editor is this endpoint's only client; it
/// re-renders straight from the PUT response rather than issuing a follow-up GET.
///
/// <para>
/// Auth: settings-endpoint parity, verified against the shipped controllers rather than assumed —
/// both <see cref="SettingsController"/> and <see cref="PersonaController"/> carry
/// <c>[Authorize(Policy = AuthorizationPolicies.Settings)]</c>, so this controller does too (the
/// format clock is station configuration, the same admin plane those two already occupy). PUT
/// requires <c>Content-Type: application/json</c> as a CSRF guard (415 otherwise), same as every
/// other admin write surface.
/// </para>
///
/// <para>
/// <see cref="IScheduleStore.ReplaceWeekAsync"/>'s own documented contract: a persona named by a
/// validated row can be deleted by a concurrent caller between this method's validation query and
/// its insert, in which case the store's FK raises and <c>ReplaceWeekAsync</c> returns
/// <see cref="ScheduleReplaceResult.PersonaVanished"/> rather than
/// <see cref="ScheduleReplaceResult.ValidationFailed"/>. This action maps that case to a generic 409 —
/// never the raw Postgres message — and asks the caller to reload and retry: by the time this fires
/// the submission that was validated is already stale, which is exactly what 409 Conflict means, and
/// reloading gets a caller a document IScheduleStore.ReplaceWeekAsync will accept.
/// <b>gh-#406 slice 1:</b> this action used to catch <c>Npgsql.PostgresException</c> directly (an L2
/// Postgres-confinement violation) and narrow the catch to SQLSTATE 23503 itself, leaving every OTHER
/// <c>PostgresException</c> to propagate to the generic 500; that narrowing now lives in
/// <c>GenWave.MediaLibrary.Station.ScheduleRepository.ReplaceWeekAsync</c> (mirrors
/// <c>PersonaRepository.DeleteAsync</c>'s own race-backstop idiom), so this controller never
/// references Npgsql at all — a store-thrown exception that ISN'T this race still propagates to the
/// generic 500 exactly as before, just from one layer down.
/// </para>
///
/// <para>
/// <b>PLAN T243 — the whole-grid PUT round-trips show identity too, alongside the dedicated
/// run-span endpoint.</b> <see cref="ScheduleSegmentDto.ShowId"/> now rides both verbs:
/// <see cref="Get"/> emits each block's current show id, and <see cref="Put"/>'s <c>ToSegment</c>
/// carries a submitted <c>showId</c> straight into <see cref="ScheduleSegment.ShowId"/> — the field
/// <see cref="IScheduleStore.ReplaceWeekAsync"/> actually writes into <c>segment_schedule.show_id</c>
/// (see <see cref="ScheduleSegment"/>'s own remarks on why <c>ShowId</c>, never a fabricated
/// <see cref="ShowSummary"/>, is what a writer sets). This closes the repaint gap the previous version
/// of this remarks paragraph named: a T129 drag-paint whole-grid repaint that echoes back the document
/// it loaded (GET's own <c>ShowId</c> included) no longer silently drops show assignments set through
/// <see cref="AssignShow"/>. <see cref="AssignShow"/> remains the ONLY wire surface with F119.2's
/// run-span semantics — a single <see cref="Put"/> row edits exactly the one block it names, never fans
/// out across a run — so a client wanting the span behavior still calls that endpoint; <see cref="Put"/>
/// is not a replacement for it, just no longer a silent eraser of its results.
/// </para>
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class ScheduleController(IScheduleStore scheduleStore, ILogger<ScheduleController> logger) : ControllerBase
{
    /// <summary>GET /api/schedule — the whole week, ordered by day then start minute (F91.1, F91.3).</summary>
    [HttpGet("schedule")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var week = await scheduleStore.LoadWeekAsync(ct);
        return Ok(ToDto(week));
    }

    /// <summary>
    /// PUT /api/schedule — atomically replace the entire week (F91.8). 200 with the fresh week
    /// document on success; 400 ProblemDetails carrying one <see cref="ScheduleCellErrorDto"/> per
    /// offending row when <see cref="IScheduleStore"/>'s app-side validation rejects the submission
    /// (nothing is written); 409 on the concurrent-persona-delete race documented on this class.
    /// </summary>
    [HttpPut("schedule")]
    [Consumes("application/json")]
    public async Task<IActionResult> Put([FromBody] ScheduleWeekDto request, CancellationToken ct)
    {
        var week = request.Segments.Select(ToSegment).ToList();

        var result = await scheduleStore.ReplaceWeekAsync(week, request.BaseVersion, ct);

        if (result is ScheduleReplaceResult.Replaced replaced)
            logger.LogInformation(
                "Schedule replaced segmentCount={SegmentCount}", replaced.Snapshot.Segments.Count);

        return result switch
        {
            ScheduleReplaceResult.Replaced r => Ok(ToDto(r.Snapshot)),
            ScheduleReplaceResult.ValidationFailed failed => BadRequest(ValidationProblem(failed.Errors)),
            ScheduleReplaceResult.VersionConflict => Conflict(StaleWeekProblem()),
            // See this class's own remarks: a validated persona id can be deleted out from under a
            // concurrent PUT between validation and insert. GenWave.MediaLibrary.Station.ScheduleRepository
            // logs the raced Postgres exception with full detail server-side (gh-#406 slice 1); this
            // controller never sees it and answers only the generic conflict, never the raw
            // SQLSTATE/message.
            ScheduleReplaceResult.PersonaVanished => Conflict(RaceConflictProblem()),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// POST /api/schedule/assign-show — SPEC F119.2's run-span show assignment (STORY-313, PLAN T243),
    /// entirely over <see cref="IScheduleStore.AssignShowAsync"/> — the ONLY wire surface with the
    /// F119.2 span rule (see this class's own remarks: <see cref="Put"/> also round-trips show identity
    /// now, but one row at a time, never fanned out across a run). 200 naming every block id the write
    /// actually touched, alongside the fresh week fingerprint (SPEC F2, gh-#255's own guard — a
    /// subsequent <see cref="Put"/> from an editor that re-rendered off THIS response's
    /// <see cref="AssignShowResponseDto.Version"/> compares cleanly against the store, the same way a
    /// GET's own <see cref="ScheduleWeekDto.Version"/> would); 400 ProblemDetails when <c>blockId</c> or
    /// a non-null <c>showId</c> names no row — mirrors
    /// <c>PersonaController.ResolvePreviewPersonaAsync</c>'s own "unknown id referenced by the request
    /// body is a 400, not a 404" posture (a body field, not a URL resource, is what's invalid). Nothing
    /// is written on either rejection.
    ///
    /// <para>
    /// <see cref="AssignShowRequestDto.ApplyToRun"/> is wire-nullable (SPEC F6): an absent field means
    /// the grid side-panel's own documented default — run-span, exactly as if <c>true</c> had been sent
    /// — never System.Text.Json's ordinary "missing non-nullable bool defaults to false" behavior, which
    /// would silently narrow every legacy/incomplete submission to the single clicked block instead.
    /// </para>
    /// </summary>
    [HttpPost("schedule/assign-show")]
    [Consumes("application/json")]
    public async Task<IActionResult> AssignShow([FromBody] AssignShowRequestDto request, CancellationToken ct)
    {
        var applyToRun = request.ApplyToRun ?? true;
        var result = await scheduleStore.AssignShowAsync(request.BlockId, request.ShowId, applyToRun, ct);

        if (result is ShowAssignResult.Assigned assigned)
            logger.LogInformation(
                "Schedule show assignment blockId={BlockId} showId={ShowId} applyToRun={ApplyToRun} updatedCount={Count}",
                request.BlockId, request.ShowId, applyToRun, assigned.UpdatedBlockIds.Count);

        return result switch
        {
            ShowAssignResult.Assigned a => Ok(new AssignShowResponseDto(a.UpdatedBlockIds, a.Version)),
            ShowAssignResult.BlockNotFound => BadRequest(UnknownBlockProblem(request.BlockId)),
            ShowAssignResult.ShowNotFound => BadRequest(UnknownShowProblem(request.ShowId)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static ScheduleWeekDto ToDto(ScheduleWeekSnapshot week) =>
        new(week.Segments.Select(ToDto).ToArray(), ScheduleWeekVersion.Compute(week.Segments));

    static ScheduleSegmentDto ToDto(ScheduleSegment segment) => new(
        segment.Id, (int)segment.Day, segment.StartMinute, segment.EndMinute,
        segment.PersonaId, segment.Genres, segment.EnergyMin, segment.EnergyMax, segment.ShowId);

    // Id is deliberately never read here — see ScheduleSegmentDto's own remarks: a submitted week is
    // always brand-new rows to the store, and PersonaId/Genres/EnergyMin/EnergyMax/ShowId are bound
    // only to the fields ScheduleSegmentDto itself declares, so nothing beyond those known fields can
    // ever reach IScheduleStore from this request body. ShowId reaches ScheduleSegment.ShowId — never
    // Show, which stays load-projection-only (ScheduleSegment's own remarks): this endpoint never
    // fabricates a ShowSummary just to carry an id through.
    static ScheduleSegment ToSegment(ScheduleSegmentDto dto) => new(
        Id: null, (DayOfWeek)dto.Day, dto.StartMinute, dto.EndMinute,
        dto.PersonaId, dto.Genres, dto.EnergyMin, dto.EnergyMax, Show: null, ShowId: dto.ShowId);

    static ProblemDetails ValidationProblem(IReadOnlyList<ScheduleCellError> errors)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "One or more schedule segments are invalid.",
            Detail = $"{errors.Count} segment(s) failed validation; nothing was saved.",
        };
        // "cellErrors", not "errors" — ASP.NET Core's automatic model-binding 400 on this same
        // endpoint+status already puts an OBJECT of string-arrays under the key "errors"; reusing
        // that key here would make the two 400 shapes indistinguishable without client-side
        // type-sniffing.
        problem.Extensions["cellErrors"] = errors.Select(ToDto).ToArray();
        return problem;
    }

    static ScheduleCellErrorDto ToDto(ScheduleCellError error) => new(
        error.RowIndex, (int)error.Day, error.StartMinute, error.EndMinute,
        KindWireValue(error.Kind), error.Message);

    // Boring camelCase strings, hand-mapped (mirrors SettingsController's ApplyModeWireValue) —
    // never System.Text.Json's numeric default for an enum-typed wire property. The trailing default
    // arm is exhaustiveness insurance for a future fifth ScheduleCellErrorKind member, same posture
    // ApplyModeWireValue takes for SettingApplyMode.
    static string KindWireValue(ScheduleCellErrorKind kind) => kind switch
    {
        ScheduleCellErrorKind.InvalidDay => "invalidDay",
        ScheduleCellErrorKind.InvalidMinuteRange => "invalidMinuteRange",
        ScheduleCellErrorKind.Overlap => "overlap",
        ScheduleCellErrorKind.UnknownPersona => "unknownPersona",
        _ => "unknown",
    };

    static ProblemDetails RaceConflictProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Schedule replace conflicted with a concurrent change.",
        Detail = "A persona referenced by this submission changed while it was being saved. Reload the schedule and try again.",
    };

    // gh-#255 — the stale-editor guard's own 409, distinguishable from the persona-race 409 above by
    // the "conflict" extension: the client keeps the operator's unsaved paint on screen and tells
    // them to reload, rather than retrying a submission that would wipe someone else's saved week.
    static ProblemDetails StaleWeekProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title  = "The schedule changed since this editor loaded it.",
            Detail = "Another tab or session saved a different week after this page loaded. "
                   + "Reload to see the latest schedule before saving — saving now would overwrite it.",
        };
        problem.Extensions["conflict"] = "staleWeek";
        return problem;
    }

    // PLAN T243 — mirrors PersonaController's own UnknownPersonaProblem/UnknownMediaProblem posture:
    // an id the REQUEST BODY names (never a URL segment) that turns out not to exist is a 400, not a
    // 404 — the request itself is the malformed thing, not a missing URL resource.
    static ProblemDetails UnknownBlockProblem(long blockId) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown schedule block.",
        Detail = $"No schedule block with id {blockId} exists.",
    };

    static ProblemDetails UnknownShowProblem(long? showId) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown show.",
        Detail = $"No show with id {showId} exists.",
    };
}
