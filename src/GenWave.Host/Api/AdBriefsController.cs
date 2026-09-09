using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

namespace GenWave.Host.Api;

/// <summary>
/// The Briefs admin surface (SPEC F162.1 — the Briefs tab: list pack + owner briefs with
/// enable/disable toggles and an add form; F162.2's own upsert key; the ratified one-owner-per-brand
/// cap, SPEC F159.1 rider; STORY-392 AC5's own API half; PLAN T403b) — <c>GET/POST /api/ad-briefs</c>,
/// <c>PATCH /api/ad-briefs/{id}</c>. <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Curation"/>, the exact <see cref="AdsController"/> precedent one
/// admin surface over (SPEC F159.1's own briefs live in the SAME "shaping the library" plane an ad
/// spot does).
///
/// <para>
/// <b>PLAN T432's own compile bridge (brand → sponsor, not this task's redesign):</b> the wire
/// contract stays exactly what it was — the request/response still say <c>brand</c>, never
/// <c>sponsorId</c> — because <see cref="AdBrief"/> itself moved to a <see cref="AdBrief.SponsorId"/>
/// foreign key under T431/T432. This controller resolves that text to (or creates) an OWNER
/// <see cref="Sponsor"/> through <see cref="ISponsorStore"/> on every call, and reads
/// <see cref="Sponsor.Name"/> back out to keep serving <c>brand</c> on the wire. The real sponsor-first
/// contract (<c>sponsorId</c> in, <c>sponsor {…}</c> out, no <c>brand</c> anywhere) is PLAN T435 —
/// this bridge exists only so the solution compiles and behaves sensibly until T435 lands.
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
/// updating (SPEC F159.1 rider, SPEC F171.6, PLAN T403b/T432's own ruling).</b>
/// <see cref="IAdBriefStore.UpsertAsync"/> (T398) stays exactly what it always was — an
/// insert-or-update seam for a future pack-install caller — but this controller never calls it: a
/// caller-facing POST that silently overwrote an existing owner brief for the same
/// (sponsor, premise) would violate STORY-392 AC5's own "surfaces as 409, not a silent write" demand.
/// <see cref="Create"/> calls <see cref="IAdBriefStore.CreateOwnerAsync"/> instead, whose own
/// <c>ON CONFLICT ... DO NOTHING</c> makes the cap check atomic — no exists-then-insert race. Since
/// T431/T432 moved the collision key from <c>(pack_slug, brand)</c> to
/// <c>(sponsor_id, premise_key)</c>, a SECOND owner brief for the SAME brand with a DIFFERENT premise
/// is now a legal second row (SPEC F171.6 — several angles per sponsor); the 409 fires only when the
/// premise (folded, blank treated as no premise) repeats too.</item>
/// <item><b>PATCH toggles ANY brief, pack or owner (T403b's own reading of F162.1's "enable/disable
/// toggles").</b> Only CREATE is owner-only; the toggle is the operator's own lever over pack content
/// too — an installed pack brief the operator wants to silence without deleting it. No brand/pack_slug
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
    // -----------------------------------------------------------------------
    // GET /api/ad-briefs — the full, unpaged list (see the class remarks)
    // -----------------------------------------------------------------------

    /// <summary>GET /api/ad-briefs (SPEC F162.1) — every brief, pack and owner alike, newest-created
    /// first. 200 with a bare <c>AdBriefDto[]</c> — see the class remarks for why this list is
    /// deliberately unpaged.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var briefs = await briefStore.ListAllAsync(ct);

        // TWO extra round trips, not one per row (round-3 finding R4 — corrects this comment's own
        // former undercount): ISponsorStore.ListAsync is itself two queries, see
        // SponsorRepository.ListAsync's own remarks. The compile bridge's own "brand" wire field
        // reads off Sponsor.Name only, so every sponsor's name is fetched once here rather than N+1
        // GetAsync calls; the per-sponsor brief/show/spot-state counts ListAsync's own second round
        // trip computes are simply unused by this list, not a correctness gap.
        var sponsors = await sponsorStore.ListAsync(q: null, ct);
        var brandById = sponsors.ToDictionary(row => row.Sponsor.Id, row => row.Sponsor.Name);

        return Ok(briefs.Select(brief => ToDto(brief, BrandFor(brief.SponsorId, brandById))).ToArray());
    }

    // -----------------------------------------------------------------------
    // POST /api/ad-briefs — owner brief create, 409 on a duplicate angle
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ad-briefs (SPEC F162.1's add form, F159.1's ratified cap; STORY-392 AC5; PLAN T432's
    /// compile bridge) — creates a new owner-authored brief. Requires <c>brand</c>;
    /// <c>premise</c>/<c>tone</c>/<c>structure</c> are all optional hints. <c>brand</c> resolves to (or
    /// creates) an owner <see cref="Sponsor"/> — see the class remarks — before the brief itself is
    /// written. 201 with the created row on success (review finding F2: a bare
    /// <see cref="StatusCodeResult"/>-and-body, NOT <see cref="ControllerBase.Created(string, object)"/>
    /// — this surface deliberately ships no <c>GET /api/ad-briefs/{id}</c>, so a <c>Location</c> header
    /// naming that route would point at a 405; the row is already in hand in the response body, and
    /// <see cref="List"/> is how a caller re-reads it later); 400 when <c>brand</c> fails the sponsor
    /// name's own db/46 <c>CHECK</c> (1–120 chars trimmed); 409 when an owner brief for this
    /// SAME brand AND SAME (folded) premise already exists (see the class remarks — a different
    /// premise for the same brand is a legal second row, SPEC F171.6).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] AdBriefCreateRequest request, CancellationToken ct)
    {
        var brand = request.Brand?.Trim();
        if (string.IsNullOrEmpty(brand))
            return BadRequest(RequiredFieldProblem("brand"));

        var premise = string.IsNullOrWhiteSpace(request.Premise) ? null : request.Premise.Trim();
        var tone = string.IsNullOrWhiteSpace(request.Tone) ? null : request.Tone.Trim();
        var structure = string.IsNullOrWhiteSpace(request.Structure) ? null : request.Structure.Trim();
        var enabled = request.Enabled ?? true;

        var (sponsorId, sponsorName, sponsorError) = await ResolveOwnerSponsorAsync(brand, ct);
        if (sponsorError is not null)
            return sponsorError;

        var created = await briefStore.CreateOwnerAsync(sponsorId, premise, tone, structure, enabled, ct);
        if (created is null)
            return Conflict(DuplicateOwnerBriefProblem(brand));

        logger.LogInformation(
            "Ad brief created id={Id} source=owner sponsorId={SponsorId} brand={Brand}",
            created.Id, created.SponsorId, LogSanitize.Strip(sponsorName));

        return StatusCode(StatusCodes.Status201Created, ToDto(created, sponsorName));
    }

    // -----------------------------------------------------------------------
    // PATCH /api/ad-briefs/{id} — the enable/disable toggle
    // -----------------------------------------------------------------------

    /// <summary>
    /// PATCH /api/ad-briefs/{id} (SPEC F162.1's enable/disable toggle) — flips <c>enabled</c> on any
    /// brief, pack or owner alike (see the class remarks). Requires <c>enabled</c> in the body (a
    /// missing value is a 400, never read as "leave unchanged" — see
    /// <see cref="AdBriefPatchRequest"/>'s own remarks). 200 with the updated row; 404 for an unknown
    /// id.
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
        var brand = sponsor?.Name ?? string.Empty;

        return Ok(ToDto(updated, brand));
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// PLAN T432 round-3 finding R3: resolves <paramref name="brand"/> text to an owner
    /// <see cref="Sponsor"/>'s id and name via <see cref="ISponsorStore.FindOrCreateOwnerAsync"/> — the
    /// store now owns the whole find-or-create/concurrent-insert-race contract this method used to
    /// hand-roll via <c>CreateOwnerAsync</c> + a <c>ListAsync</c> fallback scan (kept as a second live
    /// copy of <c>AdsController.ResolveOwnerSponsorAsync</c> rather than extracted — see that method's
    /// own remarks). Both <see cref="Sponsor.Id"/> and <see cref="Sponsor.Name"/> are returned, unlike
    /// <c>AdsController</c>'s own id-only copy, since <see cref="Create"/> also needs the name for its
    /// own log line and response body.
    ///
    /// <para>
    /// Mirrors the SAME <c>(non-nullable values, IActionResult? Error)</c> tuple shape
    /// <c>AdsController.ResolveIfMatch</c>/<c>ResolveOwnerSponsorAsync</c> use (CONTRIBUTING.md's
    /// no-null-forgiving-operator rule): the <c>0</c>/<c>""</c> returned alongside a non-null
    /// <see cref="IActionResult"/> error are never read — every call site returns immediately once
    /// <c>Error is not null</c>.
    /// </para>
    /// </summary>
    async Task<(long SponsorId, string SponsorName, IActionResult? Error)> ResolveOwnerSponsorAsync(
        string brand, CancellationToken ct)
    {
        var result = await sponsorStore.FindOrCreateOwnerAsync(brand, ct);
        return result switch
        {
            SponsorWriteResult.Ok ok => (ok.Sponsor.Id, ok.Sponsor.Name, null),
            SponsorWriteResult.InvalidField => (0, "", BadRequest(InvalidBrandProblem())),
            _ => throw new UnreachableException(
                $"ISponsorStore.FindOrCreateOwnerAsync returned {result.GetType().Name}, which its own XML doc says never happens."),
        };
    }

    static string BrandFor(long sponsorId, IReadOnlyDictionary<long, string> brandById) =>
        // station.ad_brief.sponsor_id is NOT NULL with ON DELETE RESTRICT (db/46) — every brief's
        // sponsor is always present in a full, unfiltered sponsor list; the fallback is defensive only.
        brandById.TryGetValue(sponsorId, out var brand) ? brand : string.Empty;

    static AdBriefDto ToDto(AdBrief brief, string brand) => new(
        brief.Id, brief.PackSlug, brand, brief.Premise, brief.Tone, brief.Structure, brief.Enabled,
        brief.CreatedAt);

    static ProblemDetails RequiredFieldProblem(string field) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = $"{field} is required.",
        Extensions = { ["field"] = field },
    };

    static ProblemDetails InvalidBrandProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "brand must be between 1 and 120 characters once trimmed.",
        Extensions = { ["field"] = "brand" },
    };

    static ProblemDetails DuplicateOwnerBriefProblem(string brand) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Detail = $"An owner-authored brief for brand \"{brand}\" with this premise already exists — edit it instead of creating a second one.",
        Extensions = { ["field"] = "brand" },
    };
}
