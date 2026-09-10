using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

namespace GenWave.Host.Api;

/// <summary>
/// The Sponsors admin surface (SPEC F171.2, F171.3, F171.4, F171.5; STORY-407, STORY-408, STORY-409,
/// STORY-410; PLAN T434) — <c>GET/POST /api/sponsors</c>, <c>GET/PATCH /api/sponsors/{id}</c>,
/// <c>POST /api/sponsors/{id}/pause</c>, <c>POST /api/sponsors/{id}/resume</c>,
/// <c>DELETE /api/sponsors/{id}</c>. <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Curation"/> — SPEC F171.2's own prose says "Operator," but a
/// sponsor is a library row an operator SHAPES, so <c>Curation</c> (the "media, libraries, ratings,
/// re-enrichment" plane <see cref="AuthorizationPolicies"/>'s own remarks name for it, and what
/// <see cref="AdsController"/>/<see cref="AdBriefsController"/> already carry for the sponsor they each
/// resolve on every call) is the concrete policy that wording actually names.
///
/// <para>
/// <b>The <c>packSlug</c>-forbidden design decision (STORY-408 AC3).</b> This project carries no
/// <c>[JsonExtensionData]</c>/strict-unmapped-member precedent anywhere (verified by grep across
/// <c>src/GenWave.Host</c>) — every other admin surface's request DTOs are plain nullable-property
/// records, and a caller-supplied-but-unmapped JSON property is silently dropped by
/// <c>System.Text.Json</c>'s default behavior everywhere else in this codebase. Rather than introduce
/// a first-of-its-kind strict-deserialization mechanism for one field, <see cref="SponsorCreateRequest.PackSlug"/>
/// is declared as an ordinary <see langword="string"/>? property THAT EXISTS ON THE SHAPE — any
/// non-null value (the caller supplied it, whatever the value) is refused outright by
/// <see cref="Create"/>, before any store call. A pack-owned sponsor is created only through
/// <see cref="ISponsorStore.UpsertPackSponsorsAsync"/> (the pack-install path), never through this
/// admin-facing POST.
/// </para>
///
/// <para>
/// <b><c>ETag</c> helpers now the shared <see cref="WeakETag"/> seam (T434 round-2 review finding
/// F3).</b> This controller and <see cref="AdsController"/> both call <see cref="WeakETag.Format"/>/
/// <see cref="WeakETag.TryParseVersion"/> rather than each carrying its own byte-identical copy; each
/// controller's own <see cref="ResolveIfMatch"/> stays local only for the 428/400 <c>ProblemDetails</c>
/// wording it maps the shared outcome onto. <c>MediaController</c> is the one remaining un-migrated
/// copy — see <see cref="WeakETag"/>'s own remarks for why.
/// </para>
///
/// <para>
/// <b>409, not 412, for a stale <c>If-Match</c> on <c>PATCH</c> (T434 round-2 review finding F1).</b>
/// <see cref="SponsorWriteResult.VersionConflict"/> maps to 409 <c>Conflict</c> —
/// <c>sponsor_version_conflict</c> — matching every other optimistic-concurrency write in this API
/// (<c>MediaController</c>, <see cref="AdsController.Update"/>, <c>ScheduleController</c>,
/// <c>PronunciationsController</c>, <c>SettingsController</c>): none of them reach for 412, so Sponsors
/// does not either.
/// </para>
///
/// <para>
/// <b>No null-forgiving operator (CONTRIBUTING.md).</b> <see cref="ResolveIfMatch"/> mirrors
/// <see cref="AdsController.ResolveIfMatch"/>'s own <c>(non-nullable value, IActionResult? Error)</c>
/// tuple shape; every discriminated-union result (<see cref="SponsorWriteResult"/>,
/// <see cref="SponsorDeleteResult"/>) is mapped via an exhaustive <c>switch</c> expression, never a
/// null-forgiving read of a "known to be present" field.
/// </para>
/// </summary>
[ApiController]
[Route("api/sponsors")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class SponsorsController(ISponsorStore sponsorStore, ILogger<SponsorsController> logger) : ControllerBase
{
    // ProblemDetails.Type tokens (the JinglePackController precedent) — one per distinct failure SHAPE
    // a client might branch on.
    const string NameTakenType = "sponsor_name_taken";
    const string PackSlugForbiddenType = "sponsor_pack_slug_forbidden";
    const string NamePackOwnedType = "sponsor_name_pack_owned";
    const string InUseType = "sponsor_in_use";
    const string VersionConflictType = "sponsor_version_conflict";

    // -----------------------------------------------------------------------
    // GET /api/sponsors — the full list, optionally folded-filtered
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /api/sponsors?q= (SPEC F171.2; STORY-407 AC1/AC2) — every sponsor, ordered by name, each
    /// row carrying its own referencing counts (see <see cref="SponsorListItemDto"/>'s own remarks).
    /// <paramref name="q"/>, when present, filters by folded-name substring (<c>station.sponsor_fold</c>
    /// — collapses whitespace/case, never tokenizes) — blank/whitespace-only is treated as "no
    /// filter", the same "a paging/filter value is a hint" posture <see cref="AdsController.List"/>
    /// holds for its own query parameters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, CancellationToken ct)
    {
        var effectiveQ = string.IsNullOrWhiteSpace(q) ? null : q;
        var rows = await sponsorStore.ListAsync(effectiveQ, ct);
        return Ok(rows.Select(ToListItemDto).ToList());
    }

    // -----------------------------------------------------------------------
    // GET /api/sponsors/{id} — single row, with ETag
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /api/sponsors/{id} (SPEC F171.2; STORY-407 AC3) — 200 with the row and a weak <c>ETag</c>
    /// derived from <see cref="Sponsor.Version"/> (<see cref="WeakETag.Format"/>) for a subsequent
    /// <c>PATCH</c>'s own <c>If-Match</c>; 404 for an unknown id.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var sponsor = await sponsorStore.GetAsync(id, ct);
        if (sponsor is null)
            return NotFound();

        Response.Headers.ETag = WeakETag.Format(sponsor.Version);
        return Ok(ToDto(sponsor));
    }

    // -----------------------------------------------------------------------
    // POST /api/sponsors — owner-authored create
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/sponsors (SPEC F171.3; STORY-408 AC1, AC3, AC5) — creates a new owner-authored
    /// sponsor (<see cref="Sponsor.PackSlug"/> always <see langword="null"/> on the result). Requires
    /// <c>name</c>; every other fact is optional and lands <see langword="null"/> when omitted. A
    /// request carrying ANY non-null <c>packSlug</c> is refused outright — see the class remarks. A
    /// folded-name collision is 409 <c>sponsor_name_taken</c>; a shape violation on any other field
    /// (e.g. <c>website</c> not matching <c>^https?://</c>) is 400 naming the field.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] SponsorCreateRequest request, CancellationToken ct)
    {
        if (request.PackSlug is not null)
            return BadRequest(PackSlugForbiddenProblem());

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(RequiredFieldProblem("name"));

        var newSponsor = new NewSponsor(
            name, Trimmed(request.Tagline), Trimmed(request.About), Trimmed(request.Phone),
            Trimmed(request.Address), Trimmed(request.Website), Trimmed(request.Tone));

        var result = await sponsorStore.CreateOwnerAsync(newSponsor, ct);
        return result switch
        {
            SponsorWriteResult.Ok ok => CreatedResult(ok.Sponsor),
            SponsorWriteResult.NameTaken => Conflict(NameTakenProblem()),
            SponsorWriteResult.InvalidField invalid => BadRequest(FieldProblem(invalid.FieldName, "is invalid.")),
            _ => throw new UnreachableException(
                $"ISponsorStore.CreateOwnerAsync returned {result.GetType().Name}, which its own XML doc says never happens."),
        };
    }

    // -----------------------------------------------------------------------
    // PATCH /api/sponsors/{id} — sparse edit, If-Match guarded
    // -----------------------------------------------------------------------

    /// <summary>
    /// PATCH /api/sponsors/{id} (SPEC F171.3; STORY-408 AC2, AC4) — sparse edit; <see langword="null"/>
    /// fields are left unchanged (<c>SponsorEdit</c>'s own sparse-PATCH precedent), a blank string
    /// clears an optional fact back to <see langword="null"/> (see <c>SponsorEdit</c>'s own remarks —
    /// this controller passes each optional field through UNCOLLAPSED so that distinction survives to
    /// the store). Requires <c>If-Match</c> (see <see cref="ResolveIfMatch"/>): absent → 428, malformed
    /// → 400, stale (<see cref="SponsorWriteResult.VersionConflict"/>) → 409
    /// <c>sponsor_version_conflict</c> (matching <see cref="AdsController.Update"/>'s own 409). Renaming
    /// a pack-owned sponsor (<see cref="Sponsor.PackSlug"/> non-null) is refused as 400
    /// <c>sponsor_name_pack_owned</c> — its OTHER facts stay editable in a following PATCH that omits
    /// <c>name</c>.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Update(long id, [FromBody] SponsorPatchRequest request, CancellationToken ct)
    {
        var (expectedVersion, ifMatchError) = ResolveIfMatch();
        if (ifMatchError is not null)
            return ifMatchError;

        // A blank-but-present name is read as "no change" (the AdsController.Update Title precedent
        // one controller over) — name is a required-when-present fact, not one of the optional facts
        // below that a blank string is allowed to clear.
        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();

        var edit = new SponsorEdit(
            name, request.Tagline, request.About, request.Phone, request.Address, request.Website, request.Tone);

        var result = await sponsorStore.UpdateAsync(id, edit, expectedVersion, ct);
        return result switch
        {
            SponsorWriteResult.Ok ok => Success(ok.Sponsor),
            SponsorWriteResult.NotFound => NotFound(),
            SponsorWriteResult.NamePackOwned => BadRequest(NamePackOwnedProblem()),
            SponsorWriteResult.VersionConflict => Conflict(VersionConflictProblem()),
            SponsorWriteResult.NameTaken => Conflict(NameTakenProblem()),
            SponsorWriteResult.InvalidField invalid => BadRequest(FieldProblem(invalid.FieldName, "is invalid.")),
            _ => throw new UnreachableException(
                $"ISponsorStore.UpdateAsync returned {result.GetType().Name}, which its own XML doc says never happens."),
        };
    }

    // -----------------------------------------------------------------------
    // POST /api/sponsors/{id}/pause and /resume — idempotent
    // -----------------------------------------------------------------------

    /// <summary>POST /api/sponsors/{id}/pause (SPEC F171.4; STORY-409 AC1, AC3) — idempotent; 200 with
    /// <c>paused: true</c> whether this is the first pause or a repeat one. 404 for an unknown
    /// id.</summary>
    [HttpPost("{id:long}/pause")]
    public Task<IActionResult> Pause(long id, CancellationToken ct) => SetPausedAsync(id, paused: true, ct);

    /// <summary>POST /api/sponsors/{id}/resume (SPEC F171.4; STORY-409 AC2, AC3) — idempotent; 200 with
    /// <c>paused: false</c> whether this is the first resume or a repeat one. 404 for an unknown
    /// id.</summary>
    [HttpPost("{id:long}/resume")]
    public Task<IActionResult> Resume(long id, CancellationToken ct) => SetPausedAsync(id, paused: false, ct);

    async Task<IActionResult> SetPausedAsync(long id, bool paused, CancellationToken ct)
    {
        var sponsor = await sponsorStore.SetPausedAsync(id, paused, ct);
        return sponsor is null ? NotFound() : Success(sponsor);
    }

    // -----------------------------------------------------------------------
    // DELETE /api/sponsors/{id} — guarded by every still-referencing row
    // -----------------------------------------------------------------------

    /// <summary>
    /// DELETE /api/sponsors/{id} (SPEC F171.5; STORY-410 AC1, AC2) — 204 when nothing references this
    /// sponsor; 404 for an unknown id; 409 <c>sponsor_in_use</c> when at least one
    /// <c>station.ad_brief</c>/<c>station.ad_spot</c>/<c>station.show</c> row still names it —
    /// <see cref="SponsorDeleteResult.InUse"/>'s own counts and up to ten referencing titles are
    /// surfaced both in <c>Detail</c> (human-readable) and <c>Extensions</c> (machine-readable),
    /// mirroring <see cref="JinglePackController"/>'s own in-use shape one catalog seam over. Nothing
    /// is removed on a 409 — see <c>SponsorRepository.DeleteIfUnreferencedAsync</c>'s own remarks for
    /// why the reference read happens BEFORE any DELETE is attempted.
    /// </summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var result = await sponsorStore.DeleteIfUnreferencedAsync(id, ct);
        return result switch
        {
            SponsorDeleteResult.Deleted => NoContent(),
            SponsorDeleteResult.NotFound => NotFound(),
            SponsorDeleteResult.InUse inUse => Conflict(InUseProblem(inUse)),
            _ => throw new UnreachableException(
                $"ISponsorStore.DeleteIfUnreferencedAsync returned {result.GetType().Name}, which its own XML doc says never happens."),
        };
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    IActionResult CreatedResult(Sponsor sponsor)
    {
        logger.LogInformation(
            "Sponsor created id={Id} name={Name}", sponsor.Id, LogSanitize.Strip(sponsor.Name));

        Response.Headers.ETag = WeakETag.Format(sponsor.Version);
        return Created($"/api/sponsors/{sponsor.Id}", ToDto(sponsor));
    }

    IActionResult Success(Sponsor sponsor)
    {
        Response.Headers.ETag = WeakETag.Format(sponsor.Version);
        return Ok(ToDto(sponsor));
    }

    /// <summary>Trims a request-supplied optional fact, folding blank/whitespace-only to
    /// <see langword="null"/> — the <see cref="AdBriefsController.Create"/> convention for a CREATE
    /// body's own optional fields, where "blank" and "omitted" both simply mean "nothing to
    /// store".</summary>
    static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static SponsorDto ToDto(Sponsor sponsor) => new(
        sponsor.Id, sponsor.Name, sponsor.PackSlug, sponsor.Paused, sponsor.PausedAt, sponsor.Tagline,
        sponsor.About, sponsor.Phone, sponsor.Address, sponsor.Website, sponsor.Tone, sponsor.CreatedAt,
        sponsor.UpdatedAt);

    static SponsorListItemDto ToListItemDto(SponsorListRow row) => new(
        row.Sponsor.Id, row.Sponsor.Name, row.Sponsor.PackSlug, row.Sponsor.Paused, row.Sponsor.PausedAt,
        row.Sponsor.Tagline, row.Sponsor.About, row.Sponsor.Phone, row.Sponsor.Address, row.Sponsor.Website,
        row.Sponsor.Tone, row.Sponsor.CreatedAt, row.Sponsor.UpdatedAt,
        row.Briefs, ToSpotsMap(row.SpotsByState), row.Shows);

    static IReadOnlyDictionary<string, int> ToSpotsMap(IReadOnlyDictionary<AdState, int> spotsByState) =>
        spotsByState.ToDictionary(kv => AdStateTokens.ToToken(kv.Key), kv => kv.Value);

    /// <summary>
    /// Validates the <c>If-Match</c> token via the shared <see cref="WeakETag.TryParseVersion"/> BEFORE
    /// any store call, then maps its outcome to THIS controller's own wording: absent → 428, present-
    /// but-malformed → 400, never a raw <c>PostgresException 22P02</c>. Mirrors
    /// <see cref="AdsController.ResolveIfMatch"/>'s own <c>(non-nullable value, IActionResult? Error)</c>
    /// tuple shape (CONTRIBUTING.md's no-null-forgiving-operator rule).
    /// </summary>
    (string ExpectedVersion, IActionResult? Error) ResolveIfMatch()
    {
        var raw = Request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return ("", PreconditionRequiredResult());

        return WeakETag.TryParseVersion(raw, out var version)
            ? (version, null)
            : ("", BadRequest(InvalidIfMatchProblem()));
    }

    ObjectResult PreconditionRequiredResult() =>
        StatusCode(StatusCodes.Status428PreconditionRequired, new ProblemDetails
        {
            Status = StatusCodes.Status428PreconditionRequired,
            Title  = "If-Match required.",
            Detail = "Include the ETag from GET /api/sponsors/{id} (or a prior response's own version) as the If-Match header value.",
        });

    static ProblemDetails InvalidIfMatchProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid If-Match.",
        Detail = "If-Match must carry a well-formed version token, as returned by a prior GET/save/verb response.",
    };

    static ProblemDetails VersionConflictProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = VersionConflictType,
        Detail = "The sponsor was modified since you last read it. Re-fetch and retry.",
    };

    static ProblemDetails NameTakenProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = NameTakenType,
        Detail = "A sponsor with this name already exists.",
        Extensions = { ["field"] = "name" },
    };

    static ProblemDetails PackSlugForbiddenProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Type   = PackSlugForbiddenType,
        Detail = "packSlug cannot be set from this endpoint — a pack-owned sponsor is created only by installing a pack.",
        Extensions = { ["field"] = "packSlug" },
    };

    static ProblemDetails NamePackOwnedProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Type   = NamePackOwnedType,
        Detail = "name is read-only on a pack-owned sponsor — only the installing pack may rename it.",
        Extensions = { ["field"] = "name" },
    };

    static ProblemDetails InUseProblem(SponsorDeleteResult.InUse inUse) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Sponsor is referenced.",
        Type   = InUseType,
        Detail =
            $"This sponsor cannot be deleted: it is still referenced by briefs={inUse.Briefs} " +
            $"spots={inUse.Spots} shows={inUse.Shows}. First referencing titles: {string.Join(", ", inUse.Titles)}.",
        Extensions =
        {
            ["briefs"] = inUse.Briefs,
            ["spots"] = inUse.Spots,
            ["shows"] = inUse.Shows,
            ["titles"] = inUse.Titles,
        },
    };

    static ProblemDetails RequiredFieldProblem(string field) => FieldProblem(field, "is required.");

    static ProblemDetails FieldProblem(string field, string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = $"{field} {detail}",
        Extensions = { ["field"] = field },
    };
}
