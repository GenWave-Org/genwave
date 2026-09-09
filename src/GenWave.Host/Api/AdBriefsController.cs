using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

namespace GenWave.Host.Api;

/// <summary>
/// The Briefs admin surface (SPEC F162.1 — the Briefs tab: list pack + owner briefs with
/// enable/disable toggles and an add form; SPEC F171.6 — briefs belong to a sponsor, no free-text
/// company name; STORY-392, STORY-411; PLAN T403b, T435) — <c>GET/POST /api/ad-briefs</c>,
/// <c>PATCH /api/ad-briefs/{id}</c>. <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Curation"/>, the exact <see cref="AdsController"/> precedent one
/// admin surface over (SPEC F171.6's own briefs live in the SAME "shaping the library" plane an ad
/// spot does).
///
/// <para>
/// <b>The sponsor-first contract (SPEC F171.6, STORY-411).</b> <c>POST</c> takes <c>sponsorId</c>, not
/// a free-text company name: a missing/null <c>sponsorId</c> is 400 <c>sponsor_required</c>, an unknown
/// one is 404 <c>sponsor_not_found</c> — resolved via <see cref="ISponsorStore.GetAsync"/> BEFORE the
/// insert, so an unknown id never surfaces as a raw <c>23503</c> foreign-key violation turned 500 (see
/// <see cref="Create"/>'s own remarks). Every response row carries a <see cref="SponsorRefDto"/> under
/// <c>sponsor</c> — <c>id</c>/<c>name</c>/<c>paused</c> — and no field anywhere on this wire duplicates
/// that name as a second free-text column; a brief's advertiser IS its sponsor's own <c>name</c>.
/// Uniqueness moved from the old <c>(pack_slug, free-text name)</c> pairing to
/// <c>(sponsor_id, folded premise)</c> under T431/T432: several angles per
/// sponsor are legal (SPEC F171.6), the SAME angle for the SAME sponsor twice is 409
/// <c>duplicate_brief</c>. A PAUSED sponsor still accepts a new brief — pausing withholds a sponsor's
/// spots from air (SPEC F171.4), it does not lock its briefs from editing; PLAN T440 is where pause
/// gates the ad worker's own refill sampling, not here.
/// </para>
///
/// <para>
/// <b>Rulings this task carries (PLAN T403b, documented here since <c>IAdBriefStore</c>'s own XML
/// docs already carry the store-level half of each):</b>
/// </para>
/// <list type="bullet">
/// <item><b>GET is a bare, unpaged list (T403b's own YAGNI call).</b> <see cref="AdsController.List"/>
/// pages (dozens-to-hundreds of ad spots, growing without bound over the station's lifetime); a brief
/// is an operator-curated catalog entry — dozens, not thousands — so <see cref="List"/> returns a
/// plain <c>AdBriefDto[]</c>, the <c>LibrariesController.List</c> precedent, never a
/// <c>{ items, total }</c> envelope implying paging metadata that does not exist. If the brief
/// universe ever grows past what a single unpaged read comfortably serves, that is the day this
/// method grows a <c>limit</c>/<c>offset</c> pair — not before.</item>
/// <item><b>POST creates OWNER briefs only, and 409s on a duplicate angle rather than silently
/// updating (SPEC F171.6, PLAN T403b/T432's own ruling).</b>
/// <see cref="IAdBriefStore.UpsertAsync"/> (T398) stays exactly what it always was — an
/// insert-or-update seam for a future pack-install caller — but this controller never calls it: a
/// caller-facing POST that silently overwrote an existing owner brief for the same
/// (sponsor, premise) would violate STORY-392 AC5's own "surfaces as 409, not a silent write" demand.
/// <see cref="Create"/> calls <see cref="IAdBriefStore.CreateOwnerAsync"/> instead, whose own
/// <c>ON CONFLICT ... DO NOTHING</c> makes the cap check atomic — no exists-then-insert race. Since
/// T431/T432 moved the collision key from the old <c>(pack_slug, free-text name)</c> pairing to
/// <c>(sponsor_id, premise_key)</c>, a SECOND owner brief for the SAME sponsor with a DIFFERENT premise
/// is a legal second row (SPEC F171.6 — several angles per sponsor); the 409 fires only when the
/// premise (folded, blank treated as no premise) repeats too.</item>
/// <item><b>PATCH toggles ANY brief, pack or owner (T403b's own reading of F162.1's "enable/disable
/// toggles").</b> Only CREATE is owner-only; the toggle is the operator's own lever over pack content
/// too — an installed pack brief the operator wants to silence without deleting it. No pack/owner
/// distinction is enforced here at all: <see cref="SetEnabled"/> takes only <c>id</c>.</item>
/// <item><b>No If-Match ceremony on PATCH (T403b's own YAGNI call, deliberately UNLIKE
/// <see cref="AdsController.Update"/>'s If-Match-guarded sparse edit).</b> A brief's PATCH surface is
/// exactly one boolean, and flipping it is idempotent — two concurrent toggles to the SAME value never
/// lose information, and two concurrent toggles to DIFFERENT values have no "correct" winner an ETag
/// would help pick (unlike <c>AdSpotSaveRequest</c>'s content edit, where a lost field really would be
/// lost). A plain <c>PATCH {enabled}</c> is the honest shape; the ceremony <see cref="AdsController"/>
/// carries for its OWN PATCH (weak-ETag parse, 428/400/409) buys nothing here.</item>
/// <item><b>No null-forgiving operator (CONTRIBUTING.md).</b> Every store call that can return
/// <see langword="null"/> is checked with an <c>is null</c>/<c>is not null</c> pattern before its
/// result is read, the <see cref="AdsController"/> precedent one file over.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/ad-briefs")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class AdBriefsController(
    IAdBriefStore briefStore, ISponsorStore sponsorStore, ILogger<AdBriefsController> logger) : ControllerBase
{
    // ProblemDetails.Type tokens (the SponsorsController/JinglePackController precedent) — one per
    // distinct failure SHAPE a client might branch on.
    const string SponsorRequiredType = "sponsor_required";
    const string SponsorNotFoundType = "sponsor_not_found";
    const string DuplicateBriefType = "duplicate_brief";

    // -----------------------------------------------------------------------
    // GET /api/ad-briefs — the full, unpaged list (see the class remarks)
    // -----------------------------------------------------------------------

    /// <summary>GET /api/ad-briefs (SPEC F162.1) — every brief, pack and owner alike, newest-created
    /// first. 200 with a bare <c>AdBriefDto[]</c> — see the class remarks for why this list is
    /// deliberately unpaged. Each row carries its own <c>sponsor {id,name,paused}</c> (SPEC
    /// F171.6).</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var briefs = await briefStore.ListAllAsync(ct);

        // TWO extra round trips (ISponsorStore.ListAsync's own two queries), not one per row —
        // SponsorRepository.ListAsync's own remarks: "TWO round trips, not N+1 ... the first reads
        // every matching sponsor plus its brief/show counts via left-joined per-table subqueries; the
        // second reads every matching sponsor's own ad-spot counts grouped by state". Every sponsor's
        // id/name/paused is read once here rather than N GetAsync calls, one per brief; the
        // per-sponsor brief/show/spot-state counts ListAsync's own second round trip computes are
        // simply unused by this list, not a correctness gap.
        var sponsors = await sponsorStore.ListAsync(q: null, ct);
        var sponsorsById = sponsors.ToDictionary(row => row.Sponsor.Id, row => row.Sponsor);

        return Ok(briefs.Select(brief => ToDto(brief, SponsorDtoFor(brief.SponsorId, sponsorsById))).ToArray());
    }

    // -----------------------------------------------------------------------
    // POST /api/ad-briefs — owner brief create, 409 on a duplicate angle
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ad-briefs (SPEC F162.1's add form, F171.6's sponsor-first cap; STORY-411 AC1–AC4) —
    /// creates a new owner-authored brief. Requires <c>sponsorId</c>; <c>premise</c>/<c>tone</c>/
    /// <c>structure</c> are all optional hints. 400 <c>sponsor_required</c> when <c>sponsorId</c> is
    /// missing/null; 404 <c>sponsor_not_found</c> when it names no sponsor — checked via
    /// <see cref="ISponsorStore.GetAsync"/> BEFORE <see cref="IAdBriefStore.CreateOwnerAsync"/> is ever
    /// called, so an unknown id surfaces as a clean 404 rather than the FK's own <c>23503</c> violation
    /// turning into an unhandled 500. 201 with the created row on success (review finding F2, carried
    /// forward: a bare <see cref="StatusCodeResult"/>-and-body, NOT
    /// <see cref="ControllerBase.Created(string, object)"/> — this surface deliberately ships no
    /// <c>GET /api/ad-briefs/{id}</c>, so a <c>Location</c> header naming that route would point at a
    /// 405; the row is already in hand in the response body, and <see cref="List"/> is how a caller
    /// re-reads it later); 409 <c>duplicate_brief</c> when an owner brief for this SAME sponsor AND
    /// SAME (folded) premise already exists (see the class remarks — a different premise for the same
    /// sponsor is a legal second row, SPEC F171.6). A PAUSED sponsor still accepts the brief — see the
    /// class remarks.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] AdBriefCreateRequest request, CancellationToken ct)
    {
        if (request.SponsorId is not { } sponsorId)
            return BadRequest(RequiredFieldProblem("sponsorId", SponsorRequiredType));

        // Resolved BEFORE the insert, not after: IAdBriefStore.CreateOwnerAsync's own INSERT would
        // otherwise fail an unknown sponsorId as a raw 23503 foreign-key violation, an unhandled
        // exception turning into a 500 rather than the clean 404 a caller-facing API owes.
        var sponsor = await sponsorStore.GetAsync(sponsorId, ct);
        if (sponsor is null)
            return NotFound(SponsorNotFoundProblem(sponsorId));

        var premise = string.IsNullOrWhiteSpace(request.Premise) ? null : request.Premise.Trim();
        var tone = string.IsNullOrWhiteSpace(request.Tone) ? null : request.Tone.Trim();
        var structure = string.IsNullOrWhiteSpace(request.Structure) ? null : request.Structure.Trim();
        var enabled = request.Enabled ?? true;

        // A PAUSED sponsor still accepts a new brief (see the class remarks) — no Sponsor.Paused check
        // here at all; pausing is an airing lever, not an editing lock.
        var created = await briefStore.CreateOwnerAsync(sponsorId, premise, tone, structure, enabled, ct);
        if (created is null)
            return Conflict(DuplicateBriefProblem(sponsor.Name));

        logger.LogInformation(
            "Ad brief created id={Id} source=owner sponsorId={SponsorId} sponsor={Sponsor}",
            created.Id, created.SponsorId, LogSanitize.Strip(sponsor.Name));

        return StatusCode(StatusCodes.Status201Created, ToDto(created, SponsorRefDto.From(sponsor)));
    }

    // -----------------------------------------------------------------------
    // PATCH /api/ad-briefs/{id} — the enable/disable toggle
    // -----------------------------------------------------------------------

    /// <summary>
    /// PATCH /api/ad-briefs/{id} (SPEC F162.1's enable/disable toggle) — flips <c>enabled</c> on any
    /// brief, pack or owner alike (see the class remarks). Requires <c>enabled</c> in the body (a
    /// missing value is a 400, never read as "leave unchanged" — see
    /// <see cref="AdBriefPatchRequest"/>'s own remarks). 200 with the updated row, its
    /// <c>sponsor {…}</c> included (SPEC F171.6); 404 for an unknown id.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> SetEnabled(long id, [FromBody] AdBriefPatchRequest request, CancellationToken ct)
    {
        if (request.Enabled is not { } enabled)
            return BadRequest(RequiredFieldProblem("enabled"));

        var updated = await briefStore.SetEnabledAsync(id, enabled, ct);
        if (updated is null)
            return NotFound();

        // The FK on station.ad_brief.sponsor_id is NOT NULL with ON DELETE RESTRICT (db/46) — a brief
        // can never outlive its sponsor, so this read cannot legitimately come back null; the fallback
        // is defensive only, never expected to fire.
        var sponsor = await sponsorStore.GetAsync(updated.SponsorId, ct);
        var sponsorDto = sponsor is not null ? SponsorRefDto.From(sponsor) : FallbackSponsorDto(updated.SponsorId);

        return Ok(ToDto(updated, sponsorDto));
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    static SponsorRefDto SponsorDtoFor(long sponsorId, IReadOnlyDictionary<long, Sponsor> sponsorsById) =>
        // station.ad_brief.sponsor_id is NOT NULL with ON DELETE RESTRICT (db/46) — every brief's
        // sponsor is always present in a full, unfiltered sponsor list; the fallback is defensive only.
        sponsorsById.TryGetValue(sponsorId, out var sponsor) ? SponsorRefDto.From(sponsor) : FallbackSponsorDto(sponsorId);

    static SponsorRefDto FallbackSponsorDto(long sponsorId) => new(sponsorId, string.Empty, false);

    static AdBriefDto ToDto(AdBrief brief, SponsorRefDto sponsor) => new(
        brief.Id, brief.PackSlug, sponsor, brief.Premise, brief.Tone, brief.Structure, brief.Enabled,
        brief.CreatedAt);

    // Folds the plain "{field} is required" 400 (PATCH's `enabled`, no Type — review LOW-3) and the
    // sponsor_required 400 (POST's `sponsorId`, Type set) into one factory: same Status/Title/Detail/
    // Extensions shape, `type` is the only axis that ever varied between the two call sites.
    static ProblemDetails RequiredFieldProblem(string field, string? type = null) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Type   = type,
        Detail = $"{field} is required.",
        Extensions = { ["field"] = field },
    };

    static ProblemDetails SponsorNotFoundProblem(long sponsorId) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Type   = SponsorNotFoundType,
        Detail = $"No sponsor with id {sponsorId} exists.",
        Extensions = { ["field"] = "sponsorId" },
    };

    static ProblemDetails DuplicateBriefProblem(string sponsorName) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = DuplicateBriefType,
        Detail = $"An ad brief for {sponsorName} already exists with this premise — edit it instead of creating a second one.",
        Extensions = { ["field"] = "premise" },
    };
}
