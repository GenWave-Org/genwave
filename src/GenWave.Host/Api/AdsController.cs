using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

namespace GenWave.Host.Api;

/// <summary>
/// The Ads library's own admin surface (SPEC F162.1; STORY-390 AC9, STORY-392 AC1–AC6 API half; PLAN
/// T403) — <c>GET/POST /api/ads</c>, <c>GET/PATCH /api/ads/{id}</c>,
/// <c>POST /api/ads/{id}/approve|retry|retire</c>. <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Curation"/> (the <see cref="GardenerController"/> precedent, not
/// <c>Operator</c>: an ad spot is a library row an operator SHAPES — brand, script, voice cast, state —
/// exactly the "media, libraries, ratings, re-enrichment" plane <see cref="AuthorizationPolicies"/>'s
/// own remarks name for <c>Curation</c>, not the "keeping the station on air" plane <c>Operator</c>
/// names for safe segments/TTS previews/voices).
///
/// <para>
/// <b>Rulings this task carries (PLAN T403, documented here since the interface/store XML docs already
/// carry the store-level half of each):</b>
/// </para>
/// <list type="bullet">
/// <item><b>The discard gap (SPEC F159.2's as-built rider).</b> <c>RetireAsync</c> now accepts
/// ready|draft|approved|failed — see <see cref="IAdSpotStore.RetireAsync"/>'s own remarks. Rendering
/// stays undiscardable by construction; this controller adds no extra state check of its own, since
/// the store's guarded <c>WHERE</c> already refuses (409) anything outside that set.</item>
/// <item><b>If-Match validated BEFORE it reaches SQL.</b> <see cref="ResolveIfMatch"/> strips the
/// weak-ETag wrapper (<c>MediaController.StripETagWrapper</c>'s own shape) then parses the token as an
/// unsigned 32-bit integer — Postgres's own <c>xid</c> domain — BEFORE any store call: absent → 428
/// (the <c>MediaController.Patch</c> precedent), present-but-malformed → 400 (never a raw
/// <c>PostgresException 22P02</c> the way an unvalidated token reaching <c>@expectedVersion::xid</c>
/// would produce), present-and-well-formed-but-stale/illegal-state → the store's own 409 Conflict. See
/// <see cref="ResolveIfMatch"/>'s own remarks for a filed carry-forward: <c>MediaController.Patch</c>
/// itself still carries the unvalidated version of this exact bug.</item>
/// <item><b>404-vs-409 mapped deliberately.</b> <see cref="MapTransition"/> is the one place every
/// verb below turns an <see cref="AdSpotTransitionOutcome"/> into a response:
/// <see cref="AdSpotWriteResult.NotFound"/> → 404, <see cref="AdSpotWriteResult.Conflict"/> → 409 —
/// never swapped, matching <see cref="IAdSpotStore"/>'s own (already-correct) contract.</item>
/// <item><b>PATCH edits Draft and Failed only, 409 otherwise (F162.1's own "edit drafts", PLAN T403's
/// own reading).</b> Editing an <see cref="AdState.Approved"/> spot would invalidate a render already
/// claimed or already landed; a <see cref="AdState.Failed"/> spot's script is exactly what an operator
/// fixes before a retry (<see cref="IAdSpotStore.UpdateAsync"/>'s own remarks). Enforced at the store
/// (this controller adds no redundant state check), surfaced here as 409.</item>
/// <item><b>Approve gates exactly like Retry (T403 review RULING).</b> Both re-validate the row's
/// CURRENT script before the state actually moves — <see cref="ValidateCurrentScriptThenAsync"/> is the
/// ONE shared implementation both <see cref="Approve"/> and <see cref="Retry"/> call, differing only in
/// which store transition they hand it. A brief-only draft (no <see cref="AdSpot.Script"/> yet) cannot
/// approve: <see langword="null"/> folds to <c>""</c>, which the format rule refuses outright — SPEC
/// F160.2's own <see cref="AdSpot.Brief"/> is a WRITING HINT for what the script should say, never
/// itself airable or itself validated (see <see cref="AdSpotSaveRequest.Brief"/>'s own remarks); the
/// owner must <see cref="Update"/> a real script in before either verb succeeds. A still-invalid script
/// would otherwise only reach a doomed render cycle three tasks downstream, at
/// <c>AdRenderService</c>.</item>
/// <item><b>A malformed <see cref="AdVoicePlanEntry.Tag"/>/<see cref="AdVoicePlanEntry.VoiceId"/> is a
/// save-time 400 (PLAN T403's own ruling), never a silent drop.</b> <see cref="AdRenderService"/>'s own
/// <c>ParseVoicePlan</c> drops a bad entry and degrades to the station voice — the right posture for a
/// RENDER that must never fail outright on stale/corrupted data (T401 review F2's own reasoning). This
/// editor is the OPPOSITE case: nothing has been persisted yet, so honesty is free — the owner's own
/// typo is refused here, at save, rather than silently voicing the wrong tag three steps later.</item>
/// <item><b>No null-forgiving operator (CONTRIBUTING.md).</b> <see cref="ResolveIfMatch"/> and
/// <see cref="ResolveBedMediaIdAsync"/> both return the <c>(Value, Error)</c> tuple shape
/// <c>SafeSegmentsController.ResolveBedAsync</c> already establishes one controller over, and
/// <see cref="MapTransition"/> pattern-matches <see cref="AdSpotTransitionOutcome"/> directly — every
/// call site narrows nullability through <c>is not null</c>/property patterns, never <c>!</c>.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/ads")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class AdsController(
    IAdSpotStore spotStore,
    IAdminMediaLookup adminLookup,
    IAudiencePostureProvider audiencePosture,
    ICopyBoundsProvider copyBounds,
    IPatterDurationEstimator durationEstimator,
    IOptionsMonitor<AdsOptions> adsOptions,
    ILogger<AdsController> logger) : ControllerBase
{
    const int DefaultLimit = 50;
    const int MaxLimit = 200;

    static readonly IReadOnlyList<int> AllowedSpotSeconds = [15, 30, 60];

    /// <summary>The exact wire shape <see cref="AdRenderService"/>'s own <c>VoicePlanJsonOptions</c>
    /// parses back — <see cref="AdVoicePlanEntry"/>'s reserialized-here text MUST round-trip through
    /// that reader unchanged, so this is the SAME preset (<see cref="JsonSerializerDefaults.Web"/>),
    /// never a divergent one.</summary>
    static readonly JsonSerializerOptions VoicePlanJsonOptions = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------------
    // GET /api/ads — paged, state-scoped list
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /api/ads?state=&amp;limit=&amp;offset= (SPEC F162.1, the <see cref="GardenerController.GetFindings"/>
    /// paging idiom, T385/T386's own "exact total, one round trip" discipline) — 200 with
    /// <c>{ items: AdSpotDto[], total }</c>. <paramref name="state"/> is the store's own snake_case
    /// wire text (<see cref="AdStateTokens"/>: <c>draft</c>, <c>approved</c>, <c>rendering</c>,
    /// <c>ready</c>, <c>failed</c>, <c>retired</c>); omitted means "any state". An unrecognised value
    /// is a 400 naming the field and the allowed set, never the caller's own value (the
    /// log-forging/reflection posture this whole admin surface holds). <paramref name="limit"/>
    /// defaults to <see cref="DefaultLimit"/>, clamped to [1, <see cref="MaxLimit"/>];
    /// <paramref name="offset"/> clamped to ≥ 0 — SILENTLY, never a 400 (a paging value is a hint, the
    /// <c>MediaController.List</c>/<c>GardenerController.GetFindings</c> precedent) — the store's own
    /// <c>ClampPaging</c> is what actually enforces the bound regardless of what reaches it here.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? state, [FromQuery] int? limit, [FromQuery] int? offset, CancellationToken ct)
    {
        AdState? stateFilter = null;
        if (state is not null)
        {
            if (!AdStateTokens.TryParse(state, out var parsed))
                return BadRequest(InvalidQueryValueProblem("state", AdStateTokens.Tokens));

            stateFilter = parsed;
        }

        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var effectiveOffset = Math.Max(offset ?? 0, 0);

        var page = await spotStore.ListByStateAsync(stateFilter, effectiveLimit, effectiveOffset, ct);
        return Ok(new { items = page.Items.Select(ToDto).ToList(), total = page.Total });
    }

    // -----------------------------------------------------------------------
    // GET /api/ads/{id} — single row, with ETag
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /api/ads/{id} (SPEC F162.1) — any existing row, any state (the <c>MediaController.GetById</c>/
    /// F43.1 IDOR-safe precedent: an operator opening a Failed spot to read why it failed is exactly
    /// this call). Carries a weak <c>ETag</c> derived from the row's <c>xmin</c> for
    /// <c>PATCH</c>/verb <c>If-Match</c> — see <see cref="FormatWeakETag"/>.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(id, ct);
        if (spot is null)
            return NotFound();

        Response.Headers.ETag = FormatWeakETag(spot.Version);
        return Ok(ToDto(spot));
    }

    // -----------------------------------------------------------------------
    // POST /api/ads — owner draft
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ads (SPEC F162.1, F160.4; STORY-390 AC9; STORY-392 AC2) — creates a new
    /// <see cref="AdSource.Owner"/> spot, always born <see cref="AdState.Draft"/> (never straight to
    /// Approved — <c>Station:Ads:AutoApprove</c> governs only the generation worker's own path,
    /// SPEC F159.4, never this manual editor). Requires <c>brand</c>, <c>title</c>, one of
    /// <c>brief</c>/<c>script</c>, and <c>spotSeconds</c> (one of 15/30/60). A present <c>script</c>
    /// runs the SAME validator the LLM path and pack-install preview run (SPEC F160.3, F160.4) — a
    /// violation is a 400 naming the rule; the text otherwise persists byte-for-byte verbatim, no LLM
    /// ever touching it. A present <c>bedMediaId</c> is resolved to a real row before it is ever
    /// stored (never trusted as a raw id).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] AdSpotSaveRequest request, CancellationToken ct)
    {
        var brand = request.Brand?.Trim();
        if (string.IsNullOrEmpty(brand))
            return BadRequest(RequiredFieldProblem("brand"));

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            return BadRequest(RequiredFieldProblem("title"));

        if (request.SpotSeconds is not { } spotSeconds || !AllowedSpotSeconds.Contains(spotSeconds))
            return BadRequest(SpotSecondsProblem());

        var brief = string.IsNullOrWhiteSpace(request.Brief) ? null : request.Brief.Trim();
        var script = string.IsNullOrWhiteSpace(request.Script) ? null : request.Script;
        if (brief is null && script is null)
            return BadRequest(BriefOrScriptRequiredProblem());

        if (ValidateVoicePlanEntries(request.VoicePlan) is { } voicePlanError)
            return voicePlanError;

        var (_, bedError) = await ResolveBedMediaIdAsync(request.BedMediaId, ct);
        if (bedError is not null)
            return bedError;

        if (script is not null &&
            AdScriptValidator.Validate(script, BuildValidationRequest(spotSeconds), durationEstimator)
                is AdScriptValidationResult.Refused refused)
        {
            return BadRequest(ScriptViolationProblem(refused.Violation));
        }

        var voicePlanJson = SerializeVoicePlan(request.VoicePlan);

        var spot = await spotStore.CreateAsync(
            new NewAdSpot(
                brand, title, brief, script, AdSource.Owner, PackSlug: null, spotSeconds, voicePlanJson,
                request.BedMediaId, AdState.Draft, FailReason: null),
            ct);

        logger.LogInformation(
            "Ad spot created id={Id} source=owner brand={Brand}", spot.Id, LogSanitize.Strip(spot.Brand));

        Response.Headers.ETag = FormatWeakETag(spot.Version);
        return Created($"/api/ads/{spot.Id}", ToDto(spot));
    }

    // -----------------------------------------------------------------------
    // PATCH /api/ads/{id} — owner content edit (draft/failed only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// PATCH /api/ads/{id} (SPEC F162.1, F160.4; STORY-392 AC2) — sparse content edit, legal only
    /// against <see cref="AdState.Draft"/> or <see cref="AdState.Failed"/> (409 otherwise — see the
    /// class remarks). <see langword="null"/> fields in the body are left unchanged (the
    /// <c>MediaController.Patch</c> sparse-update precedent); at least one field must be present, or
    /// this 400s before any store call — an empty <c>voicePlan: []</c> counts as ABSENT for that check
    /// (T403 review finding 4: it reserializes to <see langword="null"/> either way, so treating it as
    /// "present" would let a body carrying nothing else slip past the gate into a wasted no-op round
    /// trip). A present <c>script</c> runs the SAME validator <see cref="Create"/> runs, against the
    /// row's CURRENT <c>spotSeconds</c> when the request itself does not also change it. Requires
    /// <c>If-Match</c> (see <see cref="ResolveIfMatch"/>).
    /// </summary>
    [HttpPatch("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Update(long id, [FromBody] AdSpotSaveRequest request, CancellationToken ct)
    {
        var (expectedVersion, ifMatchError) = ResolveIfMatch();
        if (ifMatchError is not null)
            return ifMatchError;

        var brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand.Trim();
        var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        var brief = string.IsNullOrWhiteSpace(request.Brief) ? null : request.Brief.Trim();
        var script = string.IsNullOrWhiteSpace(request.Script) ? null : request.Script;

        int? spotSeconds = null;
        if (request.SpotSeconds is { } requestedSeconds)
        {
            if (!AllowedSpotSeconds.Contains(requestedSeconds))
                return BadRequest(SpotSecondsProblem());
            spotSeconds = requestedSeconds;
        }

        // An empty array reserializes to null either way (SerializeVoicePlan's own remarks) — a
        // present-but-empty voicePlan counts as ABSENT here, not as "a field to change".
        var hasVoicePlan = request.VoicePlan is { Count: > 0 };

        if (brand is null && title is null && brief is null && script is null &&
            spotSeconds is null && !hasVoicePlan && request.BedMediaId is null)
        {
            return BadRequest(NoFieldsProblem());
        }

        if (ValidateVoicePlanEntries(request.VoicePlan) is { } voicePlanError)
            return voicePlanError;

        var (_, bedError) = await ResolveBedMediaIdAsync(request.BedMediaId, ct);
        if (bedError is not null)
            return bedError;

        if (script is not null)
        {
            // The duration check needs a target length — the request's own (if it is ALSO changing
            // spotSeconds this same call) or the row's current one otherwise. Fetched here rather
            // than trusting a stale client-side value, mirroring Retry's own "read the row fresh"
            // posture just below.
            var current = await spotStore.GetByIdAsync(id, ct);
            if (current is null)
                return NotFound();

            var effectiveSpotSeconds = spotSeconds ?? current.SpotSeconds;
            if (AdScriptValidator.Validate(script, BuildValidationRequest(effectiveSpotSeconds), durationEstimator)
                is AdScriptValidationResult.Refused refused)
            {
                return BadRequest(ScriptViolationProblem(refused.Violation));
            }
        }

        var edit = new AdSpotEdit(
            brand, title, brief, script, SerializeVoicePlan(request.VoicePlan), spotSeconds, request.BedMediaId);

        var outcome = await spotStore.UpdateAsync(id, edit, expectedVersion, ct);
        return MapTransition(outcome);
    }

    // -----------------------------------------------------------------------
    // POST /api/ads/{id}/approve — Draft to Approved
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ads/{id}/approve (SPEC F159.4) — <see cref="AdState.Draft"/> to
    /// <see cref="AdState.Approved"/>, but ONLY after the row's CURRENT script re-passes the validator
    /// (T403 review RULING: approve gates exactly like <see cref="Retry"/> — see
    /// <see cref="ValidateCurrentScriptThenAsync"/>'s own remarks for the shared mechanism and the
    /// class remarks for why a brief-only draft cannot approve). Requires <c>If-Match</c>.
    /// </summary>
    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var (expectedVersion, ifMatchError) = ResolveIfMatch();
        if (ifMatchError is not null)
            return ifMatchError;

        return await ValidateCurrentScriptThenAsync(id, expectedVersion, spotStore.ApproveAsync, ct);
    }

    // -----------------------------------------------------------------------
    // POST /api/ads/{id}/retry — Failed to Approved, revalidated first
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ads/{id}/retry (SPEC F159.2's own retry) — <see cref="AdState.Failed"/> to
    /// <see cref="AdState.Approved"/>, but ONLY after the row's CURRENT script re-passes the validator
    /// (PLAN T403's own ruling: a retry with a still-invalid script would only reach a doomed render
    /// cycle three tasks downstream, at <c>AdRenderService</c>, rather than telling the operator what
    /// is wrong right now) — see <see cref="ValidateCurrentScriptThenAsync"/>'s own remarks for the
    /// full mechanism, shared verbatim with <see cref="Approve"/>. Requires <c>If-Match</c>; a refused
    /// revalidation never calls the store at all (no write attempted, so no version is spent).
    /// </summary>
    [HttpPost("{id:long}/retry")]
    public async Task<IActionResult> Retry(long id, CancellationToken ct)
    {
        var (expectedVersion, ifMatchError) = ResolveIfMatch();
        if (ifMatchError is not null)
            return ifMatchError;

        return await ValidateCurrentScriptThenAsync(id, expectedVersion, spotStore.RetryAsync, ct);
    }

    // -----------------------------------------------------------------------
    // POST /api/ads/{id}/retire — ready|draft|approved|failed to Retired
    // -----------------------------------------------------------------------

    /// <summary>POST /api/ads/{id}/retire (SPEC F159.2's as-built rider — the discard ruling, see the
    /// class remarks) — ready|draft|approved|failed to <see cref="AdState.Retired"/>. Requires
    /// <c>If-Match</c>.</summary>
    [HttpPost("{id:long}/retire")]
    public async Task<IActionResult> Retire(long id, CancellationToken ct)
    {
        var (expectedVersion, ifMatchError) = ResolveIfMatch();
        if (ifMatchError is not null)
            return ifMatchError;

        var outcome = await spotStore.RetireAsync(id, expectedVersion, ct);
        return MapTransition(outcome);
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps an xmin-guarded transition's outcome to a response, deliberately (PLAN T403 carry-forward
    /// (c)): <see cref="AdSpotWriteResult.Updated"/> → 200 + fresh ETag + body,
    /// <see cref="AdSpotWriteResult.NotFound"/> → 404, <see cref="AdSpotWriteResult.Conflict"/> → 409
    /// (stale version OR illegal FROM state — the store's own single Conflict outcome collapses both,
    /// see <see cref="IAdSpotStore"/>'s own remarks). Pattern-matches <paramref name="outcome"/>
    /// directly (T403 review finding 2, CONTRIBUTING.md's no-null-forgiving-operator rule) — the
    /// <c>{ Result: Updated, Spot: { } spot }</c> arm binds a NARROWED, non-null <c>spot</c> rather
    /// than asserting one via <c>outcome.Spot!</c>; the catch-all <c>_</c> arm covers both "unknown
    /// enum value" and the structurally-impossible "Updated with a null Spot" (<see cref="AdSpotTransitionOutcome"/>'s
    /// own contract guarantees the two travel together, but the pattern stays honest about the case it
    /// cannot itself prove away).
    /// </summary>
    IActionResult MapTransition(AdSpotTransitionOutcome outcome) => outcome switch
    {
        { Result: AdSpotWriteResult.Updated, Spot: { } spot } => Success(spot),
        { Result: AdSpotWriteResult.NotFound } => NotFound(),
        { Result: AdSpotWriteResult.Conflict } => Conflict(ConflictProblem()),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };

    IActionResult Success(AdSpot spot)
    {
        Response.Headers.ETag = FormatWeakETag(spot.Version);
        return Ok(ToDto(spot));
    }

    /// <summary>
    /// The shared "read the row fresh, re-validate its CURRENT script, then transition" mechanism
    /// <see cref="Approve"/> and <see cref="Retry"/> both call (T403 review RULING: approve gates
    /// exactly like retry) — <paramref name="transition"/> is the one thing that differs between them
    /// (<c>IAdSpotStore.ApproveAsync</c> vs <c>IAdSpotStore.RetryAsync</c>, both matching this method's
    /// own <c>(long, string, CancellationToken) → Task&lt;AdSpotTransitionOutcome&gt;</c> shape by
    /// method-group conversion — no lambda wrapper needed at either call site). A
    /// <see langword="null"/> <see cref="AdSpot.Script"/> (a brief-only draft, or a validator-failed
    /// generation — <c>AdSpotWorker.GenerateOneAsync</c>'s own remarks: "Script stays null") folds into
    /// the SAME empty-string path the format rule refuses outright, so a caller must
    /// <see cref="Update"/> a real script in before either verb can ever succeed — closing the
    /// "validator-failed generation never fix, just retry-or-approve" loop
    /// <see cref="AdScriptRuleIds.Format"/> would otherwise repeat forever. A refused revalidation
    /// never calls <paramref name="transition"/> at all — no write attempted, so no version is spent.
    /// </summary>
    async Task<IActionResult> ValidateCurrentScriptThenAsync(
        long id, string expectedVersion,
        Func<long, string, CancellationToken, Task<AdSpotTransitionOutcome>> transition, CancellationToken ct)
    {
        var current = await spotStore.GetByIdAsync(id, ct);
        if (current is null)
            return NotFound();

        if (AdScriptValidator.Validate(
                current.Script ?? "", BuildValidationRequest(current.SpotSeconds), durationEstimator)
            is AdScriptValidationResult.Refused refused)
        {
            return BadRequest(ScriptViolationProblem(refused.Violation));
        }

        return MapTransition(await transition(id, expectedVersion, ct));
    }

    AdScriptValidationRequest BuildValidationRequest(int spotSeconds) => new(
        audiencePosture.Current, copyBounds.MaxCopyChars, spotSeconds, adsOptions.CurrentValue.DurationToleranceRatio);

    /// <summary>
    /// PLAN T403's own save-time ruling (see the class remarks): a malformed entry (blank
    /// <see cref="AdVoicePlanEntry.Tag"/>/<see cref="AdVoicePlanEntry.VoiceId"/>) is refused here,
    /// never silently dropped the way <c>AdRenderService.ParseVoicePlan</c> tolerates one at render
    /// time. <see langword="null"/> (nothing to validate) returns <see langword="null"/> (no error).
    /// </summary>
    static IActionResult? ValidateVoicePlanEntries(IReadOnlyList<AdVoicePlanEntry>? plan)
    {
        if (plan is null)
            return null;

        for (var i = 0; i < plan.Count; i++)
        {
            // System.Text.Json can bind a literal JSON `null` element into this non-nullable-
            // annotated list (nullable reference types are compile-time only) — checked defensively,
            // the same TtsPreviewController.ValidateCandidates precedent.
            var entry = plan[i];
            if (entry is null)
                return new BadRequestObjectResult(FieldProblem($"voicePlan[{i}]", "must not be null."));
            if (string.IsNullOrWhiteSpace(entry.Tag))
                return new BadRequestObjectResult(FieldProblem($"voicePlan[{i}].tag", "must not be blank."));
            if (string.IsNullOrWhiteSpace(entry.VoiceId))
                return new BadRequestObjectResult(FieldProblem($"voicePlan[{i}].voiceId", "must not be blank."));
        }

        return null;
    }

    /// <summary>
    /// Resolves an optional <c>bedMediaId</c> to confirmation it names a real row — the
    /// <c>SafeSegmentsController.ResolveBedAsync</c> <c>(T?, IActionResult?)</c> tuple shape (T403
    /// review finding 2), never a raw path or a caller-trusted id either way. Returns
    /// <c>(null, null)</c> when <paramref name="bedMediaId"/> is absent (nothing to validate),
    /// <c>(bedMediaId, null)</c> when it resolves, or <c>(null, error)</c> when it does not — every
    /// call site checks <c>Error is not null</c> directly, never a null-forgiving read of the value
    /// half.
    /// </summary>
    async Task<(long? BedMediaId, IActionResult? Error)> ResolveBedMediaIdAsync(long? bedMediaId, CancellationToken ct)
    {
        if (bedMediaId is null)
            return (null, null);

        var found = await adminLookup.GetByIdWithLibraryAsync(bedMediaId.Value, ct);
        return found is null
            ? (null, BadRequest(FieldProblem("bedMediaId", $"no media row with id {bedMediaId.Value} exists.")))
            : (bedMediaId, null);
    }

    static string? SerializeVoicePlan(IReadOnlyList<AdVoicePlanEntry>? plan) =>
        plan is null or { Count: 0 } ? null : JsonSerializer.Serialize(plan, VoicePlanJsonOptions);

    /// <summary>Best-effort, never-throws read of <see cref="AdSpot.VoicePlan"/>'s opaque jsonb text
    /// back into wire shape — malformed/unparseable degrades to <see langword="null"/> rather than
    /// 500ing a GET over a row this editor itself never wrote (e.g. a pre-T403 worker-generated spot
    /// with no voice_plan at all, which is simply absent, not malformed).</summary>
    static IReadOnlyList<AdVoicePlanEntry>? DeserializeVoicePlan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<AdVoicePlanEntry>>(json, VoicePlanJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static AdSpotDto ToDto(AdSpot spot) => new(
        spot.Id, spot.Brand, spot.Title, spot.Brief, spot.Script, AdSourceTokens.ToToken(spot.Source),
        spot.PackSlug, spot.SpotSeconds, DeserializeVoicePlan(spot.VoicePlan), spot.BedMediaId,
        AdStateTokens.ToToken(spot.State), spot.FailReason, spot.MediaId, spot.CreatedAt,
        spot.StateChangedAt, spot.RenderedAt, spot.RetiredAt, spot.Version);

    /// <summary>
    /// PLAN T403 carry-forward (b): validates the <c>If-Match</c> token BEFORE it ever reaches
    /// <c>@expectedVersion::xid</c> — absent → 428 (<c>MediaController.Patch</c>'s own precedent);
    /// present but not a well-formed <c>xid</c> (Postgres's own 32-bit unsigned domain) → 400, never a
    /// raw <see cref="Npgsql.PostgresException"/> 22P02 the way an unvalidated token reaching the SQL
    /// cast would produce. The <c>(T?, IActionResult?)</c> tuple shape (T403 review finding 2,
    /// CONTRIBUTING.md's no-null-forgiving-operator rule) mirrors <c>ResolveBedMediaIdAsync</c> one
    /// method up — but here the tuple's own <c>ExpectedVersion</c> element is deliberately declared
    /// NON-nullable <c>string</c> rather than <c>string?</c>: every downstream
    /// store call (<c>ApproveAsync</c>/<c>RetryAsync</c>/<c>RetireAsync</c>/<c>UpdateAsync</c>) takes a
    /// non-nullable <c>expectedVersion</c>, and a genuinely nullable tuple element here would force
    /// either a null-forgiving read at every call site or a defensive dead-code branch the type system
    /// cannot otherwise rule out — an empty string is simply never read on the error path (every
    /// caller returns immediately once <c>Error is not null</c>), so the "one real value, or an error,
    /// never both" contract holds without leaning on <c>!</c> anywhere.
    ///
    /// <para>
    /// <b>Carry-forward (T403 review finding 9) — filed separately, not fixed here.</b>
    /// <c>MediaController.Patch</c> still carries the UNVALIDATED half of this exact bug: its own
    /// <c>StripETagWrapper</c> "returns the input unchanged if neither wrapper is present," and that
    /// raw, unvalidated token reaches <c>@expectedVersion::xid</c> directly — a malformed
    /// <c>If-Match</c> against <c>PATCH /api/media/{id}</c> today produces a raw
    /// <see cref="Npgsql.PostgresException"/> (SqlState 22P02), the SAME live bug this method closes
    /// for the Ads surface. The right altitude to close it in both places at once is a single shared
    /// <c>WeakETag</c> seam (format/strip/validate, one implementation) rather than each admin
    /// controller re-deriving its own copy — <c>AdsController</c>'s <see cref="StripETagWrapper"/>/
    /// <see cref="FormatWeakETag"/> below are already a second, byte-identical copy of
    /// <c>MediaController</c>'s own pair. Left as two live copies here, not extracted, so this task's
    /// diff stays inside the files it owns.
    /// </para>
    /// </summary>
    (string ExpectedVersion, IActionResult? Error) ResolveIfMatch()
    {
        var raw = Request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return ("", PreconditionRequiredResult());

        var stripped = StripETagWrapper(raw);
        if (!uint.TryParse(stripped, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return ("", BadRequest(InvalidIfMatchProblem()));

        return (stripped, null);
    }

    /// <summary>Mirrors <c>MediaController.StripETagWrapper</c> exactly — accepts <c>W/"&lt;token&gt;"</c>
    /// (RFC 7232 weak) or plain <c>"&lt;token&gt;"</c>; returns the input unchanged if neither wrapper
    /// is present (the subsequent <see cref="uint.TryParse"/> check in <see cref="ResolveIfMatch"/> is
    /// what actually catches that case here, unlike <c>MediaController</c>'s own unvalidated
    /// pass-through — see that method's own remarks for the filed carry-forward).</summary>
    static string StripETagWrapper(string etag)
    {
        var tag = etag.Trim();
        if (tag.StartsWith("W/\"", StringComparison.Ordinal) && tag.EndsWith('"'))
            return tag[3..^1];
        if (tag.StartsWith('"') && tag.EndsWith('"'))
            return tag[1..^1];
        return tag;
    }

    /// <summary>Mirrors <c>MediaController.FormatWeakETag</c> exactly.</summary>
    static string FormatWeakETag(string version) => $"W/\"{version}\"";

    ObjectResult PreconditionRequiredResult() =>
        StatusCode(StatusCodes.Status428PreconditionRequired, new ProblemDetails
        {
            Status = StatusCodes.Status428PreconditionRequired,
            Title  = "If-Match required.",
            Detail = "Include the ETag from GET /api/ads/{id} (or a prior response's own version) as the If-Match header value.",
        });

    static ProblemDetails InvalidIfMatchProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid If-Match.",
        Detail = "If-Match must carry a well-formed version token, as returned by a prior GET/save/verb response.",
    };

    static ProblemDetails ConflictProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Detail = "The spot was modified since you last read it, or is no longer in a state this action allows. Re-fetch and retry.",
    };

    static ProblemDetails RequiredFieldProblem(string field) => FieldProblem(field, "is required.");

    static ProblemDetails SpotSecondsProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid spotSeconds.",
        Detail = $"spotSeconds must be one of: {string.Join(", ", AllowedSpotSeconds)}.",
    };

    static ProblemDetails BriefOrScriptRequiredProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "At least one of brief or script is required.",
    };

    static ProblemDetails NoFieldsProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "At least one field must be present to edit.",
    };

    static ProblemDetails FieldProblem(string field, string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = $"{field} {detail}",
        Extensions = { ["field"] = field },
    };

    /// <summary>STORY-390 AC9's own 400 — carries the violated rule id as a machine-readable
    /// extension (<c>ruleId</c>) alongside a human <c>Detail</c>, field-qualified to <c>script</c>
    /// (SPEC F160.4's "400 + the rule id, field-level").</summary>
    static ProblemDetails ScriptViolationProblem(AdScriptViolation violation) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Script validation failed.",
        Detail = $"script {violation.Reason}",
        Extensions = { ["field"] = "script", ["ruleId"] = violation.RuleId },
    };

    static ProblemDetails InvalidQueryValueProblem(string field, IReadOnlyList<string> allowed) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = $"{field} must be one of: {string.Join(", ", allowed)}.",
    };
}
