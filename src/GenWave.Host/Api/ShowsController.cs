using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Host.Catalog;

namespace GenWave.Host.Api;

/// <summary>
/// Show CRUD for the Admin UI (SPEC F115.1, F115.4, F115.5; STORY-305, PLAN T240):
/// <c>GET/POST/PATCH/DELETE /api/shows</c> over <see cref="IShowStore"/>. Slug-addressed
/// (<c>{slug}</c>, never <c>{id}</c>) — <see cref="IShowStore.GetBySlugAsync"/>'s own remarks name it
/// as "the primitive a slug-addressed route resolves through"; every write still resolves through the
/// store's id-keyed <see cref="IShowStore.UpdateAsync"/>/<see cref="IShowStore.DeleteAsync"/>
/// internally, one extra <see cref="IShowStore.GetBySlugAsync"/> read ahead of each.
///
/// <para>
/// <b>Delete guard (SPEC F115.4) — two independent reference kinds, two different postures.</b>
/// <see cref="IShowStore.DeleteAsync"/>'s own <see cref="ShowWriteResult.Referenced"/> case fires only
/// for a real FK (<c>station.segment_schedule.show_id</c>, <c>ON DELETE RESTRICT</c>) — <see cref="Delete"/>
/// re-queries that table (<see cref="IScheduleStore.GetSlotsByShowIdAsync"/>) to NAME the blocking
/// slots in the 409 body the store's own bare case can't carry (mirrors <c>PersonaController.Delete</c>'s
/// pre-T121 posture — see <see cref="ShowWriteResult.Referenced"/>'s own remarks).
/// <c>library.media.show_id</c> (show-scoped imaging, F117.1) carries NO FK at all, so it can never
/// block the DELETE statement itself — it is handled entirely on the OTHER side of a successful
/// delete instead: <see cref="IShowImagingScope.UnscopeAsync"/> only ever runs after
/// <see cref="ShowWriteResult.Deleted"/> comes back, clearing every imaging row that named the
/// now-gone show and naming what it cleared in the response body.
/// </para>
///
/// <para>
/// <b>Ordering is deliberate, not incidental.</b> A block-referenced show refuses with NOTHING
/// touched — no delete, no imaging unscope — the least-surprising outcome: the show still exists, so
/// an imaging row scoped to it is still correctly scoped, and unscoping it anyway would silently
/// orphan-clear branding off a show an operator just failed to remove. Only once the row is confirmed
/// gone does a stale <c>show_id</c> on an imaging row become the actual problem this guard exists to
/// prevent — so the unscope write happens strictly after, never before or racing the delete.
/// </para>
///
/// <para>
/// <b>SPEC F115.5 — mirrors <c>ThemeWriteGate</c>'s fail-closed posture, deliberately NOT a shared
/// type (PLAN T240, "your call").</b> An authored <see cref="Update"/> targeting a slug whose row
/// already carries a non-null <see cref="Show.ImportedFrom"/> refuses 409 before ever calling
/// <see cref="IShowStore.UpdateAsync"/> — provenance is never even offered a chance to change.
/// <c>ThemeWriteGate</c> (PLAN T207) earns its own type because it guards TWO separate controllers
/// (<c>ThemesImportController</c>/<c>ThemesSaveAsOwnController</c>) through a genuinely multi-phase
/// pipeline (bounded body read, schema-major, deserialize-as-validation, font-law) that a hand-copy
/// between two files had already drifted once. Show writes have neither: ONE controller owns both
/// verbs (no second file to drift against), and the whole authored-vs-imported gate is a single
/// predicate on an already-loaded <see cref="Show"/> — extracting a "ShowWriteGate" type for one
/// <c>if</c> would be indirection with nothing behind it (YAGNI). What THIS controller does borrow
/// from that precedent is the shared-refusal-mapping idiom: <see cref="WriteProblem"/> maps every
/// non-success <see cref="ShowWriteResult"/> case <see cref="Create"/> and <see cref="Update"/> can
/// BOTH produce, so the SAME case always yields the SAME HTTP body regardless of which write route
/// hit it — proven by this controller's own gate-parity table (mirrors PLAN T207's 7-row
/// <c>BadBodyTable</c> precedent, narrowed to this store's five app-seam gates: blank name,
/// fallback-slug name, and the three SPEC F115.1 budgets).
/// </para>
///
/// Security: deny-by-default cookie auth, the same <c>AdminSurface</c>/<c>Settings</c> pairing as
/// <see cref="PersonaController"/>/<see cref="ScheduleController"/> (this is station configuration,
/// the same admin plane). Writes require <c>Content-Type: application/json</c> (415 otherwise,
/// F18.7). <see cref="Show.Flavor"/> rides this admin DTO deliberately (SPEC F115.3: it MAY appear
/// here, the page that authors it) — the public/spectator show projection (PLAN T251) is its own,
/// narrower DTO that never adds this field.
/// </summary>
[ApiController]
[Route("api/shows")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class ShowsController(
    IShowStore showStore,
    IScheduleStore scheduleStore,
    IShowImagingScope imagingScope,
    ILogger<ShowsController> logger) : ControllerBase
{
    /// <summary>GET /api/shows — every show row, ordered by name (F115.1).</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var shows = await showStore.GetAllAsync(ct);
        return Ok(shows.Select(ToDto).ToArray());
    }

    /// <summary>GET /api/shows/{slug} — a single show. 404 for an unknown slug.</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var show = await showStore.GetBySlugAsync(slug, ct);
        return show is null ? NotFound(NotFoundProblem(slug)) : Ok(ToDto(show));
    }

    /// <summary>
    /// POST /api/shows — create an authored show. 201 with the row on success; 400 for a blank/
    /// invalid name or an over-budget field; 409 for a slug collision (F115.1).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] ShowRequest request, CancellationToken ct)
    {
        var draft = ToDraft(request);
        var result = await showStore.CreateAsync(draft, ct);

        if (result is ShowWriteResult.Created created)
            logger.LogInformation(
                "Show created id={ShowId} name={ShowName}", created.Show.Id, LogSanitize.Strip(created.Show.Name));

        return result switch
        {
            ShowWriteResult.Created c => StatusCode(StatusCodes.Status201Created, ToDto(c.Show)),
            _ => WriteProblem(result, draft.Name),
        };
    }

    /// <summary>
    /// PATCH /api/shows/{slug} — edit an existing authored show. 200 with the row on success; 404 for
    /// an unknown slug; 409 when the target is imported (SPEC F115.5 — see this class's own remarks)
    /// or when the edit's derived slug collides with another show; 400 for a blank/invalid name or an
    /// over-budget field.
    /// </summary>
    [HttpPatch("{slug}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Update(string slug, [FromBody] ShowRequest request, CancellationToken ct)
    {
        var existing = await showStore.GetBySlugAsync(slug, ct);
        if (existing is null)
            return NotFound(NotFoundProblem(slug));

        // SPEC F115.5 — the ThemeWriteGate fail-closed posture, mirrored (see class remarks): an
        // authored write never lands on an imported show's slug, so provenance is never even offered
        // a chance to change.
        //
        // Structurally this is the same read-then-write shape gh-#394 flags (GetBySlugAsync's read
        // above, then this gate's decision, then UpdateAsync's write — nothing holds a lock across
        // the gap): a concurrent import could land on this exact slug between the read and the write.
        // Provenance itself survives that race regardless — UpdateAsync never names imported_from/
        // imported_at, so it cannot overwrite whatever an interleaved import just stamped. The
        // residual exposure is narrower than #394's own: an authored edit that was ALREADY in flight
        // landing on a row that became imported a moment earlier, silently editing what a user would
        // now expect to be gate-refused. The fix rides #394, not a second one here.
        if (existing.ImportedFrom is not null)
        {
            logger.LogWarning(
                "Show update refused slug={Slug} reason=imported importedFrom={ImportedFrom}",
                LogSafeText.Sanitize(slug), LogSanitize.Strip(existing.ImportedFrom));
            return Conflict(ImportedTargetProblem(slug, existing.ImportedFrom));
        }

        var draft = ToDraft(request);
        var result = await showStore.UpdateAsync(existing.Id, draft, ct);

        if (result is ShowWriteResult.Updated updated)
            logger.LogInformation(
                "Show updated id={ShowId} name={ShowName}", updated.Show.Id, LogSanitize.Strip(updated.Show.Name));

        return result switch
        {
            ShowWriteResult.Updated u => Ok(ToDto(u.Show)),
            ShowWriteResult.NotFound => NotFound(NotFoundProblem(slug)),
            _ => WriteProblem(result, draft.Name),
        };
    }

    /// <summary>
    /// DELETE /api/shows/{slug} — remove a show (SPEC F115.4; see this class's own remarks for the
    /// full guard). 404 for an unknown slug. 409, naming every referencing schedule block, when
    /// <c>station.segment_schedule</c> still names it — nothing deleted, nothing unscoped. Otherwise:
    /// 204 when nothing else named it either, or 200 naming every show-scoped imaging row this call
    /// unscoped.
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        var existing = await showStore.GetBySlugAsync(slug, ct);
        if (existing is null)
            return NotFound(NotFoundProblem(slug));

        var result = await showStore.DeleteAsync(existing.Id, ct);

        switch (result)
        {
            case ShowWriteResult.Deleted:
                var unscoped = await UnscopeBestEffortAsync(existing.Id, slug);
                logger.LogInformation(
                    "Show deleted id={ShowId} slug={Slug} unscopedImagingCount={Count}",
                    existing.Id, LogSafeText.Sanitize(slug), unscoped.Count);
                return unscoped.Count == 0
                    ? NoContent()
                    : Ok(new ShowDeleteResponse(unscoped.Select(ToImagingDto).ToArray()));

            case ShowWriteResult.Referenced:
                var blocks = await scheduleStore.GetSlotsByShowIdAsync(existing.Id, ct);
                logger.LogWarning(
                    "Show delete refused slug={Slug} blockCount={Count}", LogSafeText.Sanitize(slug), blocks.Count);
                return Conflict(ReferencedProblem(slug, blocks));

            case ShowWriteResult.NotFound:
                // Race backstop: gone between the GetBySlugAsync read above and this DeleteAsync call.
                return NotFound(NotFoundProblem(slug));

            default:
                // Mirrors PersonaController's own unmapped-case posture (e.g. PersonaController.cs:150)
                // rather than throwing: an unmapped ShowWriteResult case here is a store/controller
                // drift bug, not a client error, but the delete itself already committed by the time
                // this runs (unlike Create/Update's WriteProblem, called before any write) — a 500
                // that still completes the response is more honest than crashing the request pipeline
                // over a case this switch cannot even reach today (ShowWriteResult's hierarchy is
                // closed to exactly the four cases handled above).
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Best-effort post-commit cleanup (SPEC F115.4) for <see cref="Delete"/>'s <c>Deleted</c> case —
    /// the show row is ALREADY gone by the time this runs (see class remarks, "ordering is
    /// deliberate"), so a failure here must never turn an already-successful delete into a 500, and
    /// must never be silently skippable either.
    ///
    /// <para>
    /// <see cref="CancellationToken.None"/>, deliberately never <see cref="Delete"/>'s own request
    /// <c>ct</c>: the delete already committed, so a client disconnecting between the delete and this
    /// call must not skip cleanup — an aborted request is not a reason to leave orphaned
    /// <c>library.media.show_id</c> rows behind.
    /// </para>
    ///
    /// <para>
    /// Broad <c>catch (Exception)</c>, not a specific Npgsql exception type: controllers in this
    /// codebase never import Npgsql (PLAN T120 review F4 — that mapping belongs to the store/
    /// repository seam), and this is a single post-commit boundary where "the write failed, for any
    /// reason" is the only distinction this action can act on anyway. The failure is logged with the
    /// show id (SPEC F115.4) so an operator can hand-recover — a one-off <c>UPDATE library.media SET
    /// show_id = NULL WHERE show_id = &lt;id&gt;</c> — rather than retrying (a retry 404s: the show is
    /// already gone). The delete itself still reports success either way (mirrors
    /// <see cref="TtsPreviewController"/>'s own broad-catch-at-a-seam-boundary precedent).
    /// </para>
    /// </summary>
    async Task<IReadOnlyList<ScopedImagingRow>> UnscopeBestEffortAsync(long showId, string slug)
    {
        try
        {
            return await imagingScope.UnscopeAsync(showId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Show imaging unscope failed after delete id={ShowId} slug={Slug} — library.media rows " +
                "may still name a deleted show; hand-recover via UPDATE library.media SET show_id = NULL",
                showId, LogSafeText.Sanitize(slug));
            return [];
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps every non-success <see cref="ShowWriteResult"/> case BOTH write actions can produce
    /// (<see cref="ShowWriteResult.InvalidName"/>/<see cref="ShowWriteResult.BudgetExceeded"/>/
    /// <see cref="ShowWriteResult.SlugConflict"/>) to the identical ProblemDetails shape — ONE method,
    /// called from <see cref="Create"/>'s switch AND <see cref="Update"/>'s switch, so the two write
    /// routes can never drift the way PLAN T207 found the two theme-write controllers had (this
    /// controller's own gate-parity table proves it stays that way — see class remarks).
    /// <see cref="ShowWriteResult.NotFound"/> is deliberately NOT here: only <see cref="Update"/> can
    /// produce it, and it needs the target's slug (unavailable to a <see cref="Create"/> failure), so
    /// it stays inline at that one call site.
    /// </summary>
    IActionResult WriteProblem(ShowWriteResult result, string name) => result switch
    {
        ShowWriteResult.InvalidName => BadRequest(InvalidNameProblem()),
        ShowWriteResult.BudgetExceeded budget => BadRequest(BudgetExceededProblem(budget.Field)),
        ShowWriteResult.SlugConflict => Conflict(SlugConflictProblem(name)),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };

    static ShowDto ToDto(Show show) =>
        new(show.Id, show.Name, show.Slug, show.Tagline, show.Flavor, show.ImportedFrom, show.ImportedAt);

    static ShowDraft ToDraft(ShowRequest request) =>
        new(request.Name?.Trim() ?? string.Empty, request.Tagline, request.Flavor);

    static ScopedImagingRowDto ToImagingDto(ScopedImagingRow row) => new(row.MediaId, row.Title);

    static ProblemDetails NotFoundProblem(string slug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No show with slug \"{slug}\" exists.",
    };

    static ProblemDetails InvalidNameProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid name.",
        Detail = "name must not be blank or whitespace, and must not slugify to the reserved value \"persona\".",
    };

    static ProblemDetails BudgetExceededProblem(ShowBudgetField field) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = $"{FieldName(field)} must be at most {BudgetFor(field)} characters.",
    };

    static string FieldName(ShowBudgetField field) => field switch
    {
        ShowBudgetField.Name => "name",
        ShowBudgetField.Tagline => "tagline",
        ShowBudgetField.Flavor => "flavor",
        _ => throw new UnreachableException($"Unhandled {nameof(ShowBudgetField)} value."),
    };

    static int BudgetFor(ShowBudgetField field) => field switch
    {
        ShowBudgetField.Name => ShowBudgets.NameMaxChars,
        ShowBudgetField.Tagline => ShowBudgets.TaglineMaxChars,
        ShowBudgetField.Flavor => ShowBudgets.FlavorMaxChars,
        _ => throw new UnreachableException($"Unhandled {nameof(ShowBudgetField)} value."),
    };

    static ProblemDetails SlugConflictProblem(string name) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Slug conflict.",
        Detail = $"Another show already uses the slug derived from \"{name}\".",
    };

    static ProblemDetails ImportedTargetProblem(string slug, string importedFrom) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Show is imported.",
        Detail =
            $"\"{slug}\" was imported (from \"{importedFrom}\") and cannot be edited as an authored " +
            "show; its provenance is left untouched.",
    };

    // SPEC F115.4 — names every referencing block the same day/time shape PersonaController.Delete's
    // own ScheduledPersonaProblem uses. The PROBLEM-BODY builders stay two separate methods
    // (mirrored, not shared: each carries its own title/detail wording, and ScheduledPersonaProblem
    // is private to PersonaController — see this controller's own class remarks on mirror-vs-share).
    // Slot FORMATTING is a different claim — ScheduledSlotText.FormatSlot is the one shared
    // implementation both this method and ScheduledPersonaProblem call into (PLAN T240 review).
    static ProblemDetails ReferencedProblem(string slug, IReadOnlyList<ScheduledSlot> blocks) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Show is scheduled.",
        Detail = blocks.Count > 0
            ? $"\"{slug}\" is still scheduled and cannot be deleted: " +
              $"{string.Join(", ", blocks.Select(ScheduledSlotText.FormatSlot))}."
            : $"\"{slug}\" still appears in the format-clock schedule and cannot be deleted while scheduled.",
    };
}
