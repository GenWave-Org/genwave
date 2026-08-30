using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;

namespace GenWave.Host.Api;

/// <summary>
/// The Library Gardener's file-action surface (SPEC F154 whole section; STORY-379; PLAN T381,
/// gh-#529) — <c>POST api/gardener/file-actions/dry-run</c> plans one of the three file actions
/// (retag/rename/move) against a catalog row and mints a plan token; <c>POST …/confirm</c> presents
/// that token back and, if it still binds, executes it. <see cref="AuthorizationPolicies.AdminOnly"/>
/// (not the <see cref="AuthorizationPolicies.Curation"/> plane <see cref="GardenerController"/>'s own
/// findings endpoints use) — this surface writes bytes to disk, not just catalog rows; library
/// administration trust, the same posture <see cref="MediaPurgeController"/> already carries for its
/// own irreversible write.
///
/// <para>
/// <b>The disabled 404 (SPEC F154.2, STORY-379 AC1) is inside the ACTION, not middleware</b> —
/// <see cref="AuthorizationPolicies.AdminOnly"/> runs first, by ASP.NET Core's own middleware order
/// (authentication/authorization before the action ever starts): an authenticated-but-wrong-plane
/// session gets 403 before this check is ever reached (AC8), exactly as intended — F154.2's own
/// "surface does not exist" posture is for an operator who never opted into file actions at all,
/// not a substitute for the admin gate.
/// </para>
///
/// <para>
/// <b>No path ever reaches a log line or a ProblemDetails <c>Detail</c></b> (F154.3's own "path
/// never echoed" rule, extended to this whole surface) — <see cref="RuleMessage"/> is a fixed,
/// per-rule sentence, never string-built from any request field. The ONE exception is the dry-run
/// 200 body itself (<see cref="DryRun"/>'s own remarks): this endpoint is AdminOnly, and F154.5 says
/// the plan response carries <c>from</c>/<c>to</c> so the operator can actually see what they are
/// about to do — a deliberate, documented exception to the "no path" rule, not an oversight.
/// </para>
/// </summary>
[ApiController]
[Route("api/gardener/file-actions")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class GardenerFileActionsController(
    IOptionsMonitor<GardenerOptions> gardenerOptions,
    IFileActionSubjectReader subjectReader,
    IFileActionPlanner planner,
    IFileActionPlanTokens tokens,
    IFileActionExecutor executor,
    TimeProvider timeProvider,
    ILogger<GardenerFileActionsController> logger) : ControllerBase
{
    /// <summary>
    /// POST api/gardener/file-actions/dry-run — body <c>{ mediaId, verb, target? }</c>. 404 when
    /// <c>Gardener:FileActions:Enabled</c> is false (naming the knob, SPEC F154.2, STORY-379 AC1);
    /// 400 for an unrecognised <c>verb</c>; 404 for an unknown <c>mediaId</c> (neither echoed); 409
    /// when the plan is refused because the computed target already exists (STORY-379 AC12's dry-run
    /// half — <see cref="FileActionRule.TargetExists"/>); 400 for every OTHER refusal — a
    /// ProblemDetails whose <c>Detail</c> is the fixed, capitalised, operator-facing sentence ALONE
    /// (no snake_case prefix — Dean's copy rule) and whose <c>rule</c> extension member carries the
    /// machine token (T381 review N3 — the SAME structured shape <see cref="Confirm"/>'s own
    /// <c>{ outcome: "refused", rule, message }</c> body carries), never a path.
    ///
    /// <para>
    /// <b>200</b> — <c>{ from, to, tagDiff: [{ field, fileValue, catalogValue }], planToken, expiresAt }</c>.
    /// <c>from</c>/<c>to</c> ARE real paths (this class's own remarks: an AdminOnly operator must see
    /// what they are about to do before confirming it — F154.5's own "the response carries the plan").
    /// </para>
    /// </summary>
    [HttpPost("dry-run")]
    [Consumes("application/json")]
    public async Task<IActionResult> DryRun([FromBody] FileActionDryRunRequest request, CancellationToken ct)
    {
        if (!gardenerOptions.CurrentValue.FileActions.Enabled)
            return NotFound(DisabledProblem());

        if (request.Verb is null || !FileActionVerbTokens.TryParse(request.Verb, out var verb))
            return BadRequest(UnknownVerbProblem());

        var subject = await subjectReader.ReadSubjectAsync(request.MediaId, ct);
        if (subject is null)
            return NotFound(UnknownMediaProblem());

        // T381 review N8: one clock read per request, reused by both Plan and Mint — an
        // implementation could otherwise straddle two different notions of "now" across the two
        // calls (the same "one clock, supplied by the caller" posture IFileActionPlanTokens' own
        // remarks already require of ITS OWN two methods, extended here to this call site).
        var now = timeProvider.GetUtcNow();
        var result = planner.Plan(subject, verb, request.Target, now);

        if (result.IsRefused)
        {
            var rule = result.Refusal.Value.Rule;
            return rule == FileActionRule.TargetExists
                ? Conflict(RefusalProblem(rule, StatusCodes.Status409Conflict))
                : BadRequest(RefusalProblem(rule, StatusCodes.Status400BadRequest));
        }

        var plan = result.Plan;
        var planToken = tokens.Mint(plan, now);

        return Ok(new
        {
            from = plan.From,
            to = plan.To,
            tagDiff = plan.TagDiff.Select(change => new
            {
                field = change.Field,
                fileValue = change.FileValue,
                catalogValue = change.CatalogValue,
            }),
            planToken,
            expiresAt = plan.ExpiresAt,
        });
    }

    /// <summary>
    /// POST api/gardener/file-actions/confirm — body <c>{ planToken }</c>. 404 when
    /// <c>Gardener:FileActions:Enabled</c> is false (same knob as <see cref="DryRun"/>); 400 for a
    /// missing token; 409 when the token is invalid or expired (STORY-379 AC7/AC14 — never
    /// distinguished from each other in the response, and never echoing the token). Otherwise the
    /// executor's own outcome maps onto the response: <c>done</c> → 200 <c>{ outcome, to }</c>;
    /// <c>conflict</c>/<c>reverted</c> → 409 <c>{ outcome }</c>; <c>refused</c> → 409 for
    /// <see cref="FileActionRule.TargetExists"/>/<see cref="FileActionRule.TargetNotADirectory"/>/
    /// <see cref="FileActionRule.LeftoverBackup"/>/<see cref="FileActionRule.CrossDevice"/>, 400 for
    /// every other (jail) rule, both as <c>{ outcome: "refused", rule, message }</c> — no path, ever;
    /// <c>busy</c> → 503 + <c>Retry-After: 30</c>; <c>failed</c> → 500 ProblemDetails, generic (the
    /// audit table has the real reason, never surfaced over the wire). One INFO log line names the
    /// media id, verb, and outcome only.
    /// </summary>
    [HttpPost("confirm")]
    [Consumes("application/json")]
    public async Task<IActionResult> Confirm([FromBody] FileActionConfirmRequest request, CancellationToken ct)
    {
        if (!gardenerOptions.CurrentValue.FileActions.Enabled)
            return NotFound(DisabledProblem());

        if (string.IsNullOrEmpty(request.PlanToken))
            return BadRequest(MissingPlanTokenProblem());

        if (!tokens.TryRead(request.PlanToken, timeProvider.GetUtcNow(), out var plan, out _))
            return Conflict(InvalidOrExpiredTokenProblem());

        var outcome = await executor.ExecuteAsync(plan, request.PlanToken, ct);

        logger.LogInformation(
            "Gardener file action confirm media={MediaId} verb={Verb} outcome={Outcome}",
            plan.MediaId, plan.Verb, FileActionOutcomeTokens.ToToken(outcome.Kind));

        return outcome switch
        {
            { Kind: FileActionOutcomeKind.Done } => Ok(new { outcome = "done", to = plan.To }),
            { Kind: FileActionOutcomeKind.Conflict } => Conflict(new { outcome = "conflict" }),
            { Kind: FileActionOutcomeKind.Reverted } => Conflict(new { outcome = "reverted" }),
            { Kind: FileActionOutcomeKind.Refused, Rule: { } rule } => RefusedOutcome(rule),
            { Kind: FileActionOutcomeKind.Busy } => Busy(),
            _ => StatusCode(StatusCodes.Status500InternalServerError, FailedProblem()),
        };
    }

    // ── Response shaping ────────────────────────────────────────────────────

    IActionResult RefusedOutcome(FileActionRule rule)
    {
        var body = new { outcome = "refused", rule = FileActionRuleTokens.ToToken(rule), message = RuleMessage(rule) };
        return rule is FileActionRule.TargetExists or FileActionRule.TargetNotADirectory
            or FileActionRule.LeftoverBackup or FileActionRule.CrossDevice
            ? Conflict(body)
            : BadRequest(body);
    }

    IActionResult Busy()
    {
        // T381's own choice (the spec names the status + header only): the SAME { outcome } shape
        // every other outcome above carries, rather than a bespoke ProblemDetails — one consistent
        // wire contract the Gardener page's own fetcher switches on.
        Response.Headers.RetryAfter = "30";
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { outcome = "busy" });
    }

    // ── Problem builders ─────────────────────────────────────────────────────

    static ProblemDetails DisabledProblem() => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "File actions are disabled.",
        Detail = "Gardener:FileActions:Enabled is false — set it to true to use this endpoint.",
    };

    /// <summary>Names the allowed set — never the caller's own value (log-forging/reflection
    /// posture, matches <c>GardenerController.InvalidQueryValueProblem</c>).</summary>
    static ProblemDetails UnknownVerbProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "verb must be one of: retag, rename, move.",
    };

    /// <summary>No id echo (T381's own posture for this endpoint, distinct from
    /// <c>GardenerController.NotFoundProblem</c>'s numeric-only echo) — this surface writes files, so
    /// it stays deliberately quieter about what it was asked to touch.</summary>
    static ProblemDetails UnknownMediaProblem() => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = "No media row with that id exists.",
    };

    static ProblemDetails MissingPlanTokenProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "planToken is required.",
    };

    static ProblemDetails InvalidOrExpiredTokenProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "The plan is no longer valid.",
        Detail = "The plan token is invalid or has expired — request a new dry run.",
    };

    static ProblemDetails FailedProblem() => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title  = "The file action failed.",
        Detail = "The action failed; the audit log has the outcome.",
    };

    /// <summary>
    /// T381 review N3: the SAME structured shape <c>Confirm</c>'s own <c>RefusedOutcome</c> body
    /// carries (a machine-readable <c>rule</c> token as its own field, never folded into the human
    /// text) — <c>Detail</c> is the capitalised sentence ALONE, no snake_case prefix (Dean's copy
    /// rule: a ProblemDetails <c>Detail</c> is prose an operator reads, not a wire token dressed up
    /// as one). <see cref="ProblemDetails.Extensions"/> is exactly the RFC 9457 seam for a
    /// problem-type-specific member like this.
    /// </summary>
    static ProblemDetails RefusalProblem(FileActionRule rule, int status)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title  = "File action refused.",
            Detail = RuleMessage(rule),
        };
        problem.Extensions["rule"] = FileActionRuleTokens.ToToken(rule);
        return problem;
    }

    /// <summary>One fixed, operator-facing sentence per rule (F154.3's own "path never echoed"
    /// posture, extended here to every refusal this surface can ever report) — sentences start with
    /// capitals (Dean's copy rule).</summary>
    static string RuleMessage(FileActionRule rule) => rule switch
    {
        FileActionRule.NotScannedLibrary =>
            "This row's library isn't the scanned library, so there is no root to jail this action against.",
        FileActionRule.SubjectOutsideRoot =>
            "The file's own path does not resolve inside the media root.",
        FileActionRule.Traversal =>
            "The target name contains a '..' segment, which is never allowed.",
        FileActionRule.MissingTarget =>
            "A move needs a destination directory.",
        FileActionRule.InvalidName =>
            "The rename name isn't valid — it must be a plain file name with the same extension as the source.",
        FileActionRule.OutsideRoot =>
            "The computed destination does not resolve inside the media root.",
        FileActionRule.SymlinkEscape =>
            "The computed destination resolves outside the media root once symlinks are followed.",
        FileActionRule.ExemptRoot =>
            "This path sits under a root the gardener never writes into.",
        FileActionRule.SameAsSource =>
            "The computed destination is identical to the file's current path.",
        FileActionRule.TargetNotADirectory =>
            "The move's destination directory does not already exist.",
        FileActionRule.TargetExists =>
            "Something already exists at the destination.",
        FileActionRule.NothingToRetag =>
            "The catalog and the file's own tags already agree — there is nothing to retag.",
        FileActionRule.SymlinkedTarget =>
            "The move's destination directory is reached through a symlink, which is never allowed.",
        FileActionRule.CrossDevice =>
            "The source and destination are on different filesystem devices, so this move cannot complete atomically.",
        FileActionRule.LeftoverBackup =>
            "A .gwbak backup from a failed action sits beside the file and must be resolved by hand.",
        _ => "The action was refused.",
    };
}
