using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

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
/// its insert, in which case the store's FK raises and <c>ReplaceWeekAsync</c> throws
/// <see cref="PostgresException"/> rather than returning
/// <see cref="ScheduleReplaceResult.ValidationFailed"/>. This action maps ONLY that specific
/// SQLSTATE (23503, foreign-key violation — the house idiom, e.g. <c>MediaRatingRepository</c>) to a
/// generic 409 — never the raw Postgres message — and asks the caller to reload and retry: by the
/// time this fires the submission that was validated is already stale, which is exactly what 409
/// Conflict means, and reloading gets a caller a document IScheduleStore.ReplaceWeekAsync will
/// accept. Every OTHER <see cref="PostgresException"/> (permission errors, disk full, a CHECK/EXCLUDE
/// violation that means a real validation bug) is NOT the persona race and is left to propagate to
/// the generic 500 — folding it into "reload and try again" would hide a fault that reloading can't
/// fix.
/// </para>
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class ScheduleController(IScheduleStore scheduleStore, ILogger<ScheduleController> logger) : ControllerBase
{
    // Postgres SQLSTATE — well-known constant; no Npgsql.PostgresErrorCodes dependency (house idiom,
    // e.g. MediaRatingRepository.cs).
    const string ForeignKeyViolation = "23503";

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

        ScheduleReplaceResult result;
        try
        {
            result = await scheduleStore.ReplaceWeekAsync(week, request.BaseVersion, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            // See this class's own remarks: a validated persona id can be deleted out from under a
            // concurrent PUT between validation and insert. Logged with full detail server-side;
            // the client gets a generic conflict, never the raw SQLSTATE/message. Any OTHER
            // PostgresException (wrong SQLSTATE) is NOT this race and is left to propagate to the
            // generic 500 below.
            logger.LogWarning(ex,
                "Schedule replace raced a concurrent write (likely a persona deleted mid-validation)");
            return Conflict(RaceConflictProblem());
        }

        if (result is ScheduleReplaceResult.Replaced replaced)
            logger.LogInformation(
                "Schedule replaced segmentCount={SegmentCount}", replaced.Snapshot.Segments.Count);

        return result switch
        {
            ScheduleReplaceResult.Replaced r => Ok(ToDto(r.Snapshot)),
            ScheduleReplaceResult.ValidationFailed failed => BadRequest(ValidationProblem(failed.Errors)),
            ScheduleReplaceResult.VersionConflict => Conflict(StaleWeekProblem()),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static ScheduleWeekDto ToDto(ScheduleWeekSnapshot week) =>
        new(week.Segments.Select(ToDto).ToArray(), ScheduleWeekVersion.Compute(week.Segments));

    static ScheduleSegmentDto ToDto(ScheduleSegment segment) => new(
        segment.Id, (int)segment.Day, segment.StartMinute, segment.EndMinute,
        segment.PersonaId, segment.Genres, segment.EnergyMin, segment.EnergyMax);

    // Id is deliberately never read here — see ScheduleSegmentDto's own remarks: a submitted week is
    // always brand-new rows to the store, and PersonaId/Genres/EnergyMin/EnergyMax are bound only to
    // the fields ScheduleSegmentDto itself declares, so nothing beyond those known fields can ever
    // reach IScheduleStore from this request body.
    static ScheduleSegment ToSegment(ScheduleSegmentDto dto) => new(
        Id: null, (DayOfWeek)dto.Day, dto.StartMinute, dto.EndMinute,
        dto.PersonaId, dto.Genres, dto.EnergyMin, dto.EnergyMax);

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
}
