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
/// <item><b>POST creates OWNER briefs only, and 409s on a duplicate brand rather than silently
/// updating (SPEC F159.1 rider, PLAN T403b's own ruling).</b> <see cref="IAdBriefStore.UpsertAsync"/>
/// (T398) stays exactly what it always was — an insert-or-update seam for a future pack-install
/// caller — but this controller never calls it: a caller-facing POST that silently overwrote an
/// existing owner brief for the same brand would violate STORY-392 AC5's own "surfaces as 409, not a
/// silent write" demand. <see cref="Create"/> calls <see cref="IAdBriefStore.CreateOwnerAsync"/>
/// instead, whose own <c>ON CONFLICT ... DO NOTHING</c> makes the cap check atomic — no
/// exists-then-insert race.</item>
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
public sealed class AdBriefsController(IAdBriefStore briefStore, ILogger<AdBriefsController> logger) : ControllerBase
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
        return Ok(briefs.Select(ToDto).ToArray());
    }

    // -----------------------------------------------------------------------
    // POST /api/ad-briefs — owner brief create, 409 on a duplicate brand
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ad-briefs (SPEC F162.1's add form, F159.1's ratified cap; STORY-392 AC5) — creates a
    /// new owner-authored brief. Requires <c>brand</c>; <c>premise</c>/<c>tone</c>/<c>structure</c> are
    /// all optional hints. 201 with the created row on success (review finding F2: a bare
    /// <see cref="StatusCodeResult"/>-and-body, NOT <see cref="ControllerBase.Created(string, object)"/>
    /// — this surface deliberately ships no <c>GET /api/ad-briefs/{id}</c>, so a <c>Location</c> header
    /// naming that route would point at a 405; the row is already in hand in the response body, and
    /// <see cref="List"/> is how a caller re-reads it later); 409 when an owner brief for this
    /// brand already exists (see the class remarks — a pack brief for the SAME brand name never
    /// conflicts, since the cap is scoped to <c>(pack_slug, brand)</c>, not brand alone).
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

        var created = await briefStore.CreateOwnerAsync(brand, premise, tone, structure, enabled, ct);
        if (created is null)
            return Conflict(DuplicateOwnerBriefProblem(brand));

        logger.LogInformation(
            "Ad brief created id={Id} source=owner brand={Brand}", created.Id, LogSanitize.Strip(created.Brand));

        return StatusCode(StatusCodes.Status201Created, ToDto(created));
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
        return updated is not null ? Ok(ToDto(updated)) : NotFound();
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    static AdBriefDto ToDto(AdBrief brief) => new(
        brief.Id, brief.PackSlug, brief.Brand, brief.Premise, brief.Tone, brief.Structure, brief.Enabled,
        brief.CreatedAt);

    static ProblemDetails RequiredFieldProblem(string field) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = $"{field} is required.",
        Extensions = { ["field"] = field },
    };

    static ProblemDetails DuplicateOwnerBriefProblem(string brand) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Detail = $"An owner-authored brief for brand \"{brand}\" already exists — edit it instead of creating a second one.",
        Extensions = { ["field"] = "brand" },
    };
}
