using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

namespace GenWave.Host.Api;

/// <summary>
/// A show's own "deep cuts" rotation rule (SPEC F152.3, F152.5, STORY-373, PLAN T362): install/tune
/// the rule (<c>PUT /api/shows/{id}</c>), the live pool-size chip
/// (<c>GET /api/shows/{id}/rotation-pool</c>), and the last-airing line
/// (<c>GET /api/shows/{id}/last-airing</c>).
///
/// <para>
/// <b>Split out of <see cref="ShowsController"/> (T362 review LOW-5).</b> The rotation routes'
/// own five collaborators (<see cref="IStationScopeProvider"/>, <see cref="IStationDefaultEnvelopeSource"/>,
/// <see cref="IMediaCatalog"/>, <see cref="IMediaRotationSink"/>, <see cref="IBoothLogReader"/>) share
/// nothing with <see cref="ShowsController"/>'s own name/tagline/flavor CRUD or its F118 import shell —
/// folding all five into ShowsController's own constructor left an 11-dependency controller serving three genuinely separate
/// concerns. Mirrors <see cref="ThemesImportController"/> splitting off <see cref="ThemesController"/>'s
/// own CRUD (a different collaborator set, its own file) and <see cref="ExplicitOverrideController"/>
/// splitting off <see cref="MediaController"/>'s own <c>IAdminMediaWrite</c> (the identical
/// interface-segregation reasoning that class's own remarks give). Same <c>[Route("api/shows")]</c>
/// prefix, same <see cref="AdminSurfaceAttribute"/>/<see cref="AuthorizationPolicies.Settings"/>
/// pairing as <see cref="ShowsController"/> — this is still the identical station-configuration admin
/// plane, just a second controller class serving a disjoint slice of the same resource path (ASP.NET
/// Core routes by template+verb, not by controller class, so two controllers sharing one route prefix
/// is ordinary, not a workaround — <see cref="ShowsController"/>'s own id-addressed routes and this
/// controller's never collide on template+verb). The CONTRIBUTING.md L9 announce-token scheme
/// allowlist is unaffected — this controller lists no authentication scheme at all, the same deny-by-
/// default cookie posture <see cref="ShowsController"/> already documents.
/// </para>
/// </summary>
[ApiController]
[Route("api/shows")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class ShowRotationController(
    IShowStore showStore,
    IStationScopeProvider scopeProvider,
    IStationDefaultEnvelopeSource defaultEnvelopeSource,
    IMediaCatalog catalog,
    IMediaRotationSink rotationSink,
    IBoothLogReader boothLogReader,
    ILogger<ShowRotationController> logger) : ControllerBase
{
    /// <summary>
    /// PUT /api/shows/{id} — install or tune a show's own "deep cuts" rotation rule (SPEC F152.3,
    /// F152.5, STORY-373, PLAN T362). Body: <c>{ "rotation": {"maxPlays": int?, "notAiredWithinDays":
    /// int?} | null }</c> — a THIRD, deliberately narrower write than <see cref="ShowsController.Create"/>/
    /// <see cref="ShowsController.Update"/>'s name/tagline/flavor pair, id-addressed (not
    /// slug-addressed: this route has no name to derive a slug from) rather than folded into
    /// <see cref="ShowsController.Update"/>'s own PATCH body, mirroring how
    /// <see cref="Core.Abstractions.IShowStore.SetRotationAsync"/> is already its own store method
    /// rather than a parameter <see cref="Core.Abstractions.IShowStore.UpdateAsync"/> grew.
    ///
    /// <para>
    /// <b>Absent vs. explicit null (SPEC F152.5) — the <see cref="ExplicitOverrideController.SetExplicit"/>
    /// idiom, mirrored.</b> A <c>rotation</c>-typed DTO property cannot distinguish "the client omitted
    /// the field" from "the client sent <c>null</c>" (both bind to the same C# default), so this
    /// action reads a raw <see cref="JsonElement"/> body instead: the property ABSENT means "leave the
    /// existing rule exactly as it stands" (<see cref="ParseRotationBody"/>'s own
    /// <see cref="ShowRotationBodyResult.Unchanged"/> case — this action never calls
    /// <see cref="Core.Abstractions.IShowStore.SetRotationAsync"/> for it, it simply re-reads and
    /// echoes the show); the property present as JSON <c>null</c> means "remove the rule"
    /// (<see cref="ShowRotationBodyResult.Cleared"/>). THIS is the one place a bare <c>rotation: null</c>
    /// means CLEAR — <see cref="Core.Abstractions.IShowStore.ImportAsync"/>'s own <c>rotation</c>
    /// parameter (PLAN T363) reads the identical explicit-<c>null</c> shape as NO OPINION instead (an
    /// operator's own PUT can un-rule a show; a catalog card re-import can not — see that method's own
    /// remarks for why).
    /// </para>
    ///
    /// <para>
    /// <b>Validation (SPEC F152.5) — 400 naming the field, never a silent clamp.</b> A present, non-null
    /// <c>rotation</c> object must set at least one of <c>maxPlays</c>/<c>notAiredWithinDays</c>;
    /// <c>maxPlays</c>, when set, must be ≥ 0; <c>notAiredWithinDays</c>, when set, must fall in
    /// 1–3650. All three run in <see cref="ParseRotationBody"/>, entirely before this action ever
    /// calls the store.
    /// </para>
    ///
    /// <para>
    /// <b>No SPEC F115.5 provenance gate here — deliberately, unlike <see cref="ShowsController.Update"/>'s
    /// own.</b> <see cref="Core.Abstractions.IShowStore.SetRotationAsync"/> itself carries no
    /// authored-vs-imported check (see that method's own remarks: "this method performs no
    /// name/slug/budget validation of its own"), and this action does not add one either — SPEC
    /// F152.6's own Deep Cuts card SHIPS imported (<c>imported_from</c> non-null by construction), so
    /// gating rotation edits behind F115.5's authored-only rule would make the shipped card's own rule
    /// permanently un-tunable the moment it lands on a station. Rotation is an operator knob riding a
    /// DORMANT column (SPEC F115.2), not an identity field the way name/tagline/flavor are — F115.5's
    /// posture is untouched for those three; this route simply never extends it to a fourth.
    /// </para>
    ///
    /// 404 when <paramref name="id"/> names no show. A successful write raises
    /// <see cref="Core.Abstractions.IShowStore.ShowChanged"/> (inside
    /// <see cref="Core.Abstractions.IShowStore.SetRotationAsync"/> itself), so
    /// <c>CachingScheduleResolver</c>'s cached snapshot dirties and the next resolution sees the edit
    /// with no restart (SPEC F152.3's T360 amendment).
    /// </summary>
    [HttpPut("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> SetRotation(long id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var parsed = ParseRotationBody(body);
        if (parsed is ShowRotationBodyResult.Invalid invalid)
            return BadRequest(RotationValidationProblem(invalid.Detail));

        if (parsed is ShowRotationBodyResult.Unchanged)
        {
            var existing = await showStore.GetByIdAsync(id, ct);
            return existing is null ? NotFound(NotFoundProblemById(id)) : Ok(ShowDto.From(existing));
        }

        var rotation = parsed is ShowRotationBodyResult.Valid valid ? valid.Rotation : null;
        var result = await showStore.SetRotationAsync(id, rotation, ct);

        if (result is ShowWriteResult.Updated updatedRotation)
            logger.LogInformation("Show rotation set id={ShowId} name={ShowName}",
                updatedRotation.Show.Id, LogSanitize.Strip(updatedRotation.Show.Name));

        return result switch
        {
            ShowWriteResult.Updated u => Ok(ShowDto.From(u.Show)),
            ShowWriteResult.NotFound => NotFound(NotFoundProblemById(id)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// GET /api/shows/{id}/rotation-pool — the Shows page's own "live pool size" chip (SPEC F152.5,
    /// STORY-373, PLAN T362): <c>{ eligible: &lt;int|null&gt;, since: &lt;Gardener:RotationSince&gt; }</c>.
    /// <c>eligible</c> is the count of playable rows the show's OWN rotation rule (whatever
    /// <see cref="Core.Domain.Show.Rotation"/> holds right now, including <see langword="null"/> — an
    /// unset rule simply widens the count to the whole station-default envelope's playable pool) admits
    /// under the STATION DEFAULT envelope — <paramref name="id"/>'s show layered onto
    /// <see cref="defaultEnvelopeSource"/>'s <c>Current</c> the exact way
    /// <c>ScheduleEnvelopeProvider</c>/the resolver's own gap-fallback already does (SPEC F152.3) —
    /// scoped to <see cref="scopeProvider"/>'s own station rotation scope, mirroring every other
    /// admin catalog read in this codebase (<c>PersonaController</c>, <c>ReenrichController</c>).
    /// <see langword="null"/> ("unknown") when <see cref="catalog"/> itself answers null (an empty
    /// scope). 404 when <paramref name="id"/> names no show.
    /// </summary>
    [HttpGet("{id:long}/rotation-pool")]
    public async Task<IActionResult> GetRotationPool(long id, CancellationToken ct)
    {
        var show = await showStore.GetByIdAsync(id, ct);
        if (show is null) return NotFound(NotFoundProblemById(id));

        var envelope = defaultEnvelopeSource.Current with { Rotation = show.Rotation };
        var eligible = await catalog.GetEnvelopeCandidateCountAsync(scopeProvider.Current, envelope, ct);
        var since = await rotationSink.GetRotationSinceAsync(ct);

        return Ok(new ShowRotationPoolDto(eligible, since));
    }

    /// <summary>
    /// GET /api/shows/{id}/last-airing — the Shows page's own "last airing: N picks, M relaxed" line
    /// (SPEC F152.5, STORY-373, PLAN T362): ALWAYS 200 with a <see cref="ShowLastAiringDto"/> body
    /// (T362 review MED-3, binding — see that DTO's own remarks for why: an earlier draft answered a
    /// bare <c>Ok(null)</c> for "never aired," which ASP.NET Core's own <c>HttpNoContentOutputFormatter</c>
    /// silently rewrote to a real 204 with NO body at all — never the JSON <c>null</c> literal this
    /// action's own doc comment claimed). <paramref name="id"/>'s show having never aired a
    /// <c>"track-started"</c> row yet (see <see cref="Core.Abstractions.IBoothLogReader.GetLastAiringAsync"/>'s
    /// own "contiguous run" definition) reads as <see cref="ShowLastAiringDto.AiredCount"/>/
    /// <see cref="ShowLastAiringDto.Relaxed"/> both <see langword="null"/>, never a distinct HTTP
    /// shape. 404 when <paramref name="id"/> names no show at all — the ONE case this route still
    /// distinguishes by status code, since "the show doesn't exist" and "the show exists but never
    /// aired" are genuinely different facts an operator can act on differently.
    /// </summary>
    [HttpGet("{id:long}/last-airing")]
    public async Task<IActionResult> GetLastAiring(long id, CancellationToken ct)
    {
        var show = await showStore.GetByIdAsync(id, ct);
        if (show is null) return NotFound(NotFoundProblemById(id));

        var lastAiring = await boothLogReader.GetLastAiringAsync(id, ct);
        return Ok(new ShowLastAiringDto(lastAiring?.Picks, lastAiring?.Relaxed));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>The id-addressed sibling of <c>ShowsController.NotFoundProblem(string)</c> — every
    /// rotation route (SPEC F152.5, PLAN T362) resolves by id, never slug, so its 404 names the id
    /// instead.</summary>
    static ProblemDetails NotFoundProblemById(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No show with id {id} exists.",
    };

    /// <summary>
    /// SPEC F152.5's own three validation rules (SetRotation's body): at least one bound set,
    /// <c>maxPlays</c> ≥ 0, <c>notAiredWithinDays</c> 1–3650, plus basic shape checks (not an object,
    /// or a member that is not a whole number). <paramref name="detail"/> already names the offending
    /// field in prose (mirrors <c>ShowsController.BudgetExceededProblem</c>'s own "{field} must
    /// be..." shape), so this builder needs nothing further from
    /// <see cref="ShowRotationBodyResult.Invalid.Field"/> beyond what <see cref="ParseRotationBody"/>
    /// already wrote into the detail text.
    /// </summary>
    static ProblemDetails RotationValidationProblem(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = detail,
    };

    /// <summary>
    /// Parses <c>PUT /api/shows/{id}</c>'s body into a <see cref="ShowRotationBodyResult"/> (SPEC
    /// F152.5) — reads a raw <see cref="JsonElement"/> rather than a typed DTO for the identical
    /// absent-vs-null reason <see cref="ExplicitOverrideController.TryParseExplicit"/> does one
    /// controller over (see <see cref="SetRotation"/>'s own remarks). A body that is not a JSON object
    /// at all, or carries no <c>"rotation"</c> property, is <see cref="ShowRotationBodyResult.Unchanged"/>
    /// — a malformed non-object BODY (rather than a malformed <c>rotation</c> VALUE) is left to ASP.NET
    /// Core's own model binder, which already 400s a body that cannot parse as JSON before this method
    /// ever runs.
    /// </summary>
    static ShowRotationBodyResult ParseRotationBody(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("rotation", out var rotationElement))
            return new ShowRotationBodyResult.Unchanged();

        if (rotationElement.ValueKind == JsonValueKind.Null)
            return new ShowRotationBodyResult.Cleared();

        if (rotationElement.ValueKind != JsonValueKind.Object)
            return new ShowRotationBodyResult.Invalid("rotation", "rotation must be an object or null.");

        if (!TryReadOptionalInt(rotationElement, "maxPlays", out var maxPlays, out var maxPlaysError))
            return new ShowRotationBodyResult.Invalid("maxPlays", maxPlaysError);

        if (!TryReadOptionalInt(rotationElement, "notAiredWithinDays", out var notAiredWithinDays, out var notAiredError))
            return new ShowRotationBodyResult.Invalid("notAiredWithinDays", notAiredError);

        // PLAN T363 review MED-3 — RotationPredicateRules is the ONE shared home for the three SPEC
        // F152.1/F152.5 rules and their literal bounds (ShowManifestParser.ParseEnvelope's own import-
        // edge gate shares it too); this action keeps its own refusal TEXT exactly as it already was,
        // only naming WHICH field failed off the shared result.
        return RotationPredicateRules.Validate(maxPlays, notAiredWithinDays) switch
        {
            RotationPredicateField.Rotation => new ShowRotationBodyResult.Invalid(
                "rotation", "rotation must set at least one of maxPlays or notAiredWithinDays."),
            RotationPredicateField.MaxPlays => new ShowRotationBodyResult.Invalid(
                "maxPlays", "maxPlays must be at least 0."),
            RotationPredicateField.NotAiredWithinDays => new ShowRotationBodyResult.Invalid(
                "notAiredWithinDays", "notAiredWithinDays must be between 1 and 3650."),
            _ => new ShowRotationBodyResult.Valid(new RotationPredicate(maxPlays, notAiredWithinDays)),
        };
    }

    /// <summary>
    /// Reads an optional whole-number property off <paramref name="rotation"/> — absent or JSON
    /// <c>null</c> yields <see langword="null"/> with no error (both members of <c>rotation</c> are
    /// individually optional, SPEC F152.5); present with any OTHER <see cref="JsonValueKind"/> (a
    /// string, a fraction, an array, …) is a shape error naming <paramref name="propertyName"/>.
    /// </summary>
    static bool TryReadOptionalInt(
        JsonElement rotation, string propertyName, out int? value, out string error)
    {
        if (!rotation.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            value = null;
            error = "";
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed))
        {
            value = parsed;
            error = "";
            return true;
        }

        value = null;
        error = $"{propertyName} must be a whole number.";
        return false;
    }
}
