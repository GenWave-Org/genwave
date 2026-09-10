using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

namespace GenWave.Host.Api;

/// <summary>
/// The Ads library's own admin surface (SPEC F162.1, F171.7; STORY-390 AC9, STORY-392 AC1–AC6 API
/// half, STORY-412; PLAN T403, T436) — <c>GET/POST /api/ads</c>, <c>GET/PATCH /api/ads/{id}</c>,
/// <c>POST /api/ads/{id}/approve|retry|retire</c>. <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Curation"/> (the <see cref="GardenerController"/> precedent, not
/// <c>Operator</c>: an ad spot is a library row an operator SHAPES — sponsor, script, voice cast, state
/// — exactly the "media, libraries, ratings, re-enrichment" plane <see cref="AuthorizationPolicies"/>'s
/// own remarks name for <c>Curation</c>, not the "keeping the station on air" plane <c>Operator</c>
/// names for safe segments/TTS previews/voices).
///
/// <para>
/// <b>The sponsor-first contract (SPEC F171.7, STORY-412; PLAN T436 — the <see cref="AdBriefsController"/>
/// precedent one admin surface over).</b> <c>POST</c>/<c>PATCH</c> take <c>sponsorId</c>, never a
/// free-text customer label: on <see cref="Create"/>, a missing/null <c>sponsorId</c> is 400
/// <c>sponsor_required</c>, an unknown one is 404 <c>sponsor_not_found</c>, and a
/// <see cref="Sponsor.Paused"/> one is 409 <c>sponsor_paused</c> — every check resolved via
/// <see cref="ISponsorStore.GetAsync"/> BEFORE the insert, so an unknown id never surfaces as a raw
/// foreign-key violation turned 500. <see cref="Update"/> checks only 404 <c>sponsor_not_found</c> — a
/// paused sponsor IS allowed on PATCH (STORY-412 AC4 gates CREATING only; pausing withholds a sponsor's
/// spots from air, SPEC F171.4, it does not lock its spots from editing) — proven directly, not merely
/// inferred from AC3's own PATCH (which moves a spot between two UNPAUSED sponsors), by
/// <c>FeatureASpotBelongsToASponsorAndSnapshotsTheName.ScenarioAPausedSponsorsExistingSpotsStayEditable
/// .PatchingAPausedSponsorsExistingSpotIs200</c>, which pauses a sponsor and then PATCHes one of its
/// existing spots. <see cref="AdSpot.SponsorName"/>
/// is a snapshot written at creation and refreshed whenever <c>PATCH</c> changes <c>sponsorId</c> (the
/// store's own <c>AdSpotRepository.CreateAsync</c>/<c>UpdateAsync</c> half of this contract); every
/// response also carries the live <see cref="SponsorRefDto"/> under <c>sponsor</c>
/// (<see cref="SponsorRefDto.From"/>) so a caller can grey out a paused sponsor's rows without a second
/// round trip. <see cref="List"/> takes an optional <c>sponsorId</c> query filter. No property anywhere
/// on this wire names a sponsor with a free-text customer label (STORY-412 AC7's own contract-scan
/// spec, which asserts exactly that).
/// </para>
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
/// <item><b>If-Match validated BEFORE it reaches SQL.</b> <see cref="ResolveIfMatch"/> calls the shared
/// <see cref="WeakETag.TryParseVersion"/> (strip the weak-ETag wrapper, then parse the token as an
/// unsigned 32-bit integer — Postgres's own <c>xid</c> domain) BEFORE any store call: absent → 428
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
/// <item><b>Owner sponsors are real; pack sponsors stay parody (SPEC F172.5, PLAN T438).</b>
/// <see cref="BuildValidationRequest"/> hands <see cref="AdScriptValidator"/> the SCRIPT'S OWN sponsor
/// — the just-resolved <c>sponsor</c> on <see cref="Create"/>, the edited-to sponsor or (one extra
/// read) the row's current sponsor on <see cref="Update"/>, the row's current sponsor on
/// <see cref="ValidateCurrentScriptThenAsync"/> — so an owner sponsor's own name/phone clear the
/// blocklist and 555 rule while a pack sponsor's script keeps refusing exactly as before.</item>
/// <item><b>No null-forgiving operator (CONTRIBUTING.md).</b> <see cref="ResolveIfMatch"/> and
/// <see cref="ResolveBedMediaIdAsync"/> both return the <c>(Value, Error)</c> tuple shape
/// <c>SafeSegmentsController.ResolveBedAsync</c> already establishes one controller over, and
/// <see cref="MapTransition"/> pattern-matches <see cref="AdSpotTransitionOutcome"/> directly — every
/// call site narrows nullability through <c>is not null</c>/property patterns, never <c>!</c>.</item>
/// </list>
///
/// <para>
/// <b>The write job (SPEC F174.2, F174.3; STORY-422, STORY-423; PLAN T441).</b>
/// <c>POST /api/ads/{id}/write</c> requires the row to be <see cref="AdState.Draft"/> — checked HERE,
/// not by <see cref="AdSpotJobService.TryEnqueueAsync"/> itself, which stamps a job for a row in ANY
/// state (its own guard is only "no job already claims this row") — an unknown id is 404, a non-draft
/// row is 409 <c>ad_write_not_draft</c>, an already-claimed row is 409 <c>ad_job_busy</c>, and a
/// station-wide queue already at <see cref="AdsOptions.JobQueueCapacity"/> is 429
/// <c>ad_job_queue_full</c>. A successful enqueue answers 202 with the row's fresh <c>job</c> object —
/// the write itself lands asynchronously; a caller polls <c>GET /api/ads/{id}</c> to see it finish.
/// <c>DELETE /api/ads/{id}/job</c> cancels whatever is queued or running for that id and clears the
/// stamp, idempotently — a row with no active job still answers 204 (<see cref="AdSpotJobService.CancelAsync"/>'s
/// own contract). <b>A process restart orphans a stamped row</b> (<see cref="AdSpotJobService"/>'s own
/// remarks state this in full) — this task leaves that gap to an operator's own <c>DELETE …/job</c>.
/// </para>
///
/// <para>
/// <b>The preview render (SPEC F174.4; STORY-424, STORY-429; PLAN T442).</b>
/// <c>POST /api/ads/{id}/preview</c> shares the SAME <see cref="AdSpotJobService.TryEnqueueAsync"/>/
/// <see cref="MapEnqueueResultAsync"/> plumbing as <c>/write</c> above, under its own two checks: an
/// empty/whitespace <see cref="AdSpot.Script"/> is 400 <c>script_required</c> (a preview has nothing to
/// render without one — an owner draft with only a <c>brief</c> has not been written yet), and a row
/// outside <see cref="AdState.Draft"/>/<see cref="AdState.Approved"/> is 409
/// <c>ad_preview_not_editable</c> — the same two states <c>/write</c>'s own draft-only gate is a
/// subset of, since a preview stays useful one step further (an approved row an operator wants one
/// more listen to before it renders for real). The rendered file never becomes a
/// <c>library.media</c> row (<see cref="GenWave.Tts.ICastSegmentAuthor.AssembleOnlyAsync"/>'s own contract) —
/// <c>GET /api/ads/{id}/preview.wav</c> streams it straight off disk instead, 404 when the spot is
/// unknown, no preview has ever been stamped, the stamped <c>preview_key</c> no longer matches a fresh
/// <see cref="AdPreviewKey.Compute"/> over the row's CURRENT inputs (STALE — an edit since the last
/// preview render), or the file itself is simply gone. <see cref="AdSpotDto.Preview"/> carries the
/// SAME staleness verdict on every read (<c>GetById</c>/<c>List</c>/every write-verb response) so a
/// caller never has to probe the <c>.wav</c> route just to know whether re-rendering makes sense.
/// </para>
/// </summary>
[ApiController]
[Route("api/ads")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class AdsController(
    IAdSpotStore spotStore,
    ISponsorStore sponsorStore,
    IAdminMediaLookup adminLookup,
    IAudiencePostureProvider audiencePosture,
    ICopyBoundsProvider copyBounds,
    IPatterDurationEstimator durationEstimator,
    IOptionsMonitor<AdsOptions> adsOptions,
    AdSpotJobService jobService,
    AdRenderService renderService,
    AdSpotLocatorRoots locatorRoots,
    IConfiguration configuration,
    ILogger<AdsController> logger) : ControllerBase
{
    const int DefaultLimit = 50;
    const int MaxLimit = 200;

    // ProblemDetails.Type tokens (the SponsorsController/AdBriefsController precedent) — one per
    // distinct failure SHAPE a client might branch on.
    const string SponsorRequiredType = "sponsor_required";
    const string SponsorNotFoundType = "sponsor_not_found";
    const string SponsorPausedType = "sponsor_paused";
    const string WriteNotDraftType = "ad_write_not_draft";
    const string ScriptRequiredType = "script_required";
    const string PreviewNotEditableType = "ad_preview_not_editable";
    const string JobBusyType = "ad_job_busy";
    const string JobQueueFullType = "ad_job_queue_full";
    const string PreviewStaleType = "preview_stale";
    const string PreviewPromotionFailedType = "preview_promotion_failed";

    static readonly IReadOnlyList<int> AllowedSpotSeconds = [15, 30, 60];

    // -----------------------------------------------------------------------
    // GET /api/ads — paged, state- and sponsor-scoped list
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /api/ads?state=&amp;sponsorId=&amp;limit=&amp;offset= (SPEC F162.1, F171.7; the
    /// <see cref="GardenerController.GetFindings"/> paging idiom, T385/T386's own "exact total, one
    /// round trip" discipline) — 200 with <c>{ items: AdSpotDto[], total }</c>. <paramref name="state"/>
    /// is the store's own snake_case wire text (<see cref="AdStateTokens"/>: <c>draft</c>,
    /// <c>approved</c>, <c>rendering</c>, <c>ready</c>, <c>failed</c>, <c>retired</c>); omitted means
    /// "any state". <paramref name="sponsorId"/> narrows to exactly that sponsor's spots (STORY-412
    /// AC6); omitted means "any sponsor". Either an unrecognised <paramref name="state"/> or a
    /// non-numeric <paramref name="sponsorId"/> is a 400 naming the field, never the caller's own value
    /// (the log-forging/reflection posture this whole admin surface holds). <paramref name="limit"/>
    /// defaults to <see cref="DefaultLimit"/>, clamped to [1, <see cref="MaxLimit"/>];
    /// <paramref name="offset"/> clamped to ≥ 0 — SILENTLY, never a 400 (a paging value is a hint, the
    /// <c>MediaController.List</c>/<c>GardenerController.GetFindings</c> precedent) — the store's own
    /// <c>ClampPaging</c> is what actually enforces the bound regardless of what reaches it here.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? state, [FromQuery] string? sponsorId, [FromQuery] int? limit,
        [FromQuery] int? offset, CancellationToken ct)
    {
        AdState? stateFilter = null;
        if (state is not null)
        {
            if (!AdStateTokens.TryParse(state, out var parsed))
                return BadRequest(InvalidQueryValueProblem("state", AdStateTokens.Tokens));

            stateFilter = parsed;
        }

        long? sponsorIdFilter = null;
        if (sponsorId is not null)
        {
            if (!long.TryParse(sponsorId, out var parsedSponsorId))
                return BadRequest(InvalidSponsorIdQueryProblem());

            sponsorIdFilter = parsedSponsorId;
        }

        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var effectiveOffset = Math.Max(offset ?? 0, 0);

        var page = await spotStore.ListByStateAsync(stateFilter, sponsorIdFilter, effectiveLimit, effectiveOffset, ct);

        // TWO extra round trips (ISponsorStore.ListAsync's own two queries), not one per row — the
        // AdBriefsController.List precedent; SponsorRepository.ListAsync's own remarks: "TWO round
        // trips, not N+1": the first reads every matching sponsor plus its brief/show counts via
        // left-joined per-table subqueries, the second reads every matching sponsor's own ad-spot
        // counts grouped by state. Every sponsor's id/name/paused is read once here, not once per row
        // via N GetAsync calls; the per-sponsor counts that round trip also computes are simply unused
        // by this list, not a correctness gap.
        var sponsors = await sponsorStore.ListAsync(q: null, ct);
        var sponsorsById = sponsors.ToDictionary(row => row.Sponsor.Id, row => row.Sponsor);

        // Read ONCE for the whole page (PLAN T442 ruling), not once per row inside the Select below —
        // ToPreviewDto's own staleness recompute still costs one SHA256 PER ROW (db/46's own ruling:
        // no stored flag, so that part of the cost is intentional), but the Live settings themselves
        // never change mid-request, so re-reading IConfiguration N times for the SAME values was pure
        // waste this hoist removes.
        var liveSettings = AdLiveSettingsReader.Read(configuration);

        return Ok(new
        {
            items = page.Items
                .Select(spot => ToDto(spot, sponsorsById.GetValueOrDefault(spot.SponsorId), liveSettings))
                .ToList(),
            total = page.Total,
        });
    }

    // -----------------------------------------------------------------------
    // GET /api/ads/{id} — single row, with ETag
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /api/ads/{id} (SPEC F162.1) — any existing row, any state (the <c>MediaController.GetById</c>/
    /// F43.1 IDOR-safe precedent: an operator opening a Failed spot to read why it failed is exactly
    /// this call). Carries a weak <c>ETag</c> derived from the row's <c>xmin</c> for
    /// <c>PATCH</c>/verb <c>If-Match</c> — see <see cref="WeakETag.Format"/>.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(id, ct);
        if (spot is null)
            return NotFound();

        var sponsor = await sponsorStore.GetAsync(spot.SponsorId, ct);
        Response.Headers.ETag = WeakETag.Format(spot.Version);
        return Ok(ToDto(spot, sponsor, AdLiveSettingsReader.Read(configuration)));
    }

    // -----------------------------------------------------------------------
    // POST /api/ads — owner draft
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ads (SPEC F162.1, F160.4, F171.7; STORY-390 AC9; STORY-392 AC2; STORY-412 AC1–AC5) —
    /// creates a new <see cref="AdSource.Owner"/> spot, always born <see cref="AdState.Draft"/> (never
    /// straight to Approved — <c>Station:Ads:AutoApprove</c> governs only the generation worker's own
    /// path, SPEC F159.4, never this manual editor). Requires <c>sponsorId</c>, <c>title</c>, one of
    /// <c>brief</c>/<c>script</c>, and <c>spotSeconds</c> (one of 15/30/60). <c>sponsorId</c> is
    /// resolved via <see cref="ISponsorStore.GetAsync"/> BEFORE any insert: missing/null → 400
    /// <c>sponsor_required</c>; unknown → 404 <c>sponsor_not_found</c>; a
    /// <see cref="Sponsor.Paused"/> sponsor → 409 <c>sponsor_paused</c> (see the class remarks — CREATE
    /// is gated, PATCH is not). A present <c>script</c> runs the SAME validator the LLM path and
    /// pack-install preview run (SPEC F160.3, F160.4) — a violation is a 400 naming the rule; the text
    /// otherwise persists byte-for-byte verbatim, no LLM ever touching it. A present <c>bedMediaId</c>
    /// is resolved to a real row before it is ever stored (never trusted as a raw id). 201 carries
    /// <c>sponsorId</c>, the freshly-stamped <c>sponsorName</c> snapshot, and the live
    /// <c>sponsor {id,name,paused}</c>.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] AdSpotSaveRequest request, CancellationToken ct)
    {
        if (request.SponsorId is not { } sponsorId)
            return BadRequest(RequiredFieldProblem("sponsorId", SponsorRequiredType));

        // Resolved BEFORE any insert (STORY-412 AC5), the AdBriefsController.Create precedent one
        // controller over — an unknown id must never reach spotStore.CreateAsync's own INSERT and
        // surface as a raw foreign-key violation turned 500.
        var sponsor = await sponsorStore.GetAsync(sponsorId, ct);
        if (sponsor is null)
            return NotFound(SponsorNotFoundProblem(sponsorId));
        if (sponsor.Paused)
            return Conflict(SponsorPausedProblem());

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
            AdScriptValidator.Validate(script, BuildValidationRequest(spotSeconds, sponsor), durationEstimator)
                is AdScriptValidationResult.Refused refused)
        {
            return BadRequest(ScriptViolationProblem(refused.Violation));
        }

        var voicePlanJson = SerializeVoicePlan(request.VoicePlan);

        var spot = await spotStore.CreateAsync(
            new NewAdSpot(
                sponsorId, title, brief, script, AdSource.Owner, PackSlug: null, spotSeconds, voicePlanJson,
                request.BedMediaId, AdState.Draft, FailReason: null),
            ct);

        logger.LogInformation(
            "Ad spot created id={Id} source=owner sponsorId={SponsorId} sponsor={Sponsor}",
            spot.Id, spot.SponsorId, LogSanitize.Strip(spot.SponsorName));

        Response.Headers.ETag = WeakETag.Format(spot.Version);
        return Created($"/api/ads/{spot.Id}", ToDto(spot, sponsor, AdLiveSettingsReader.Read(configuration)));
    }

    // -----------------------------------------------------------------------
    // PATCH /api/ads/{id} — owner content edit (draft/failed only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// PATCH /api/ads/{id} (SPEC F162.1, F160.4, F171.7; STORY-392 AC2; STORY-412 AC3) — sparse
    /// content edit, legal only against <see cref="AdState.Draft"/> or <see cref="AdState.Failed"/>
    /// (409 otherwise — see the class remarks). <see langword="null"/> fields in the body are left
    /// unchanged (the <c>MediaController.Patch</c> sparse-update precedent); at least one field must be
    /// present, or this 400s before any store call — an empty <c>voicePlan: []</c> counts as ABSENT for
    /// that check (T403 review finding 4: it reserializes to <see langword="null"/> either way, so
    /// treating it as "present" would let a body carrying nothing else slip past the gate into a wasted
    /// no-op round trip). A present <c>sponsorId</c> is resolved via <see cref="ISponsorStore.GetAsync"/>
    /// — unknown → 404 <c>sponsor_not_found</c>; unlike <see cref="Create"/>, a PAUSED sponsor is
    /// accepted here (see the class remarks — only creating a spot is gated). A present <c>script</c>
    /// runs the SAME validator <see cref="Create"/> runs, against the row's CURRENT <c>spotSeconds</c>
    /// when the request itself does not also change it. Requires <c>If-Match</c> (see
    /// <see cref="ResolveIfMatch"/>). The response's <c>sponsorName</c> reflects the store's own
    /// refreshed snapshot when <c>sponsorId</c> changed.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Update(long id, [FromBody] AdSpotSaveRequest request, CancellationToken ct)
    {
        var (expectedVersion, ifMatchError) = ResolveIfMatch();
        if (ifMatchError is not null)
            return ifMatchError;

        // Round-3 finding R3 (kept from the T432 bridge): fetched HERE, before any sponsor lookup
        // below, so a 404 (no such spot) or a stale If-Match returns immediately without a wasted
        // sponsor round trip. The real, atomic xmin guard still lives in spotStore.UpdateAsync at the
        // end of this method — this early read is a pre-check only; a race landing between this read
        // and that UPDATE is still caught there. Also serves the "if (script is not null)" branch's own
        // need for the row's CURRENT spotSeconds — one read for both gates.
        var current = await spotStore.GetByIdAsync(id, ct);
        if (current is null)
            return NotFound();
        if (current.Version != expectedVersion)
            return Conflict(ConflictProblem());

        // A paused sponsor IS allowed on PATCH (STORY-412 AC4 gates CREATING only) — no Sponsor.Paused
        // check here, unlike Create's gate above.
        Sponsor? sponsor = null;
        if (request.SponsorId is { } requestedSponsorId)
        {
            sponsor = await sponsorStore.GetAsync(requestedSponsorId, ct);
            if (sponsor is null)
                return NotFound(SponsorNotFoundProblem(requestedSponsorId));
        }

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

        // request.SponsorId is null (not sponsor is null) is the actual fact this gate needs — the
        // REQUEST carried no sponsorId at all, not "the sponsor resolved to something null" (had
        // request.SponsorId been present but unknown, the 404 above would already have returned).
        if (request.SponsorId is null && title is null && brief is null && script is null &&
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
            // spotSeconds this same call) or the row's current one otherwise. current was already
            // fetched above (the R3 pre-check), never trusting a stale client-side value.
            var effectiveSpotSeconds = spotSeconds ?? current.SpotSeconds;

            // The owner-sponsor skips (SPEC F172.5, PLAN T438 ruling) need the SPONSOR THE SCRIPT WILL
            // ACTUALLY BELONG TO once this edit lands: the just-resolved edited-to sponsor when this
            // same request also changes sponsorId, otherwise the row's current sponsor (one extra read
            // only on this path — a script edit that leaves sponsorId alone is common, an edit that
            // moves a spot between sponsors on the same call is not).
            var validationSponsor = sponsor ?? await sponsorStore.GetAsync(current.SponsorId, ct);
            if (AdScriptValidator.Validate(script, BuildValidationRequest(effectiveSpotSeconds, validationSponsor), durationEstimator)
                is AdScriptValidationResult.Refused refused)
            {
                return BadRequest(ScriptViolationProblem(refused.Violation));
            }
        }

        var edit = new AdSpotEdit(
            sponsor?.Id, title, brief, script, SerializeVoicePlan(request.VoicePlan), spotSeconds,
            request.BedMediaId);

        var outcome = await spotStore.UpdateAsync(id, edit, expectedVersion, ct);
        return await MapTransition(outcome, ct);
    }

    // -----------------------------------------------------------------------
    // POST /api/ads/{id}/approve — Draft to Approved
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ads/{id}/approve (SPEC F159.4, F174.5; STORY-425, STORY-429 AC1; PLAN T445) —
    /// <see cref="AdState.Draft"/> to <see cref="AdState.Approved"/>, but ONLY after the row's CURRENT
    /// script re-passes the validator (T403 review RULING: approve gates exactly like
    /// <see cref="Retry"/> — see <see cref="ValidateCurrentScriptThenAsync"/>'s own remarks for the
    /// shared mechanism and the class remarks for why a brief-only draft cannot approve). Requires
    /// <c>If-Match</c>.
    ///
    /// <para>
    /// <b>A stamped preview also promotes to on-air, in the SAME call (STORY-425).</b> A row carrying
    /// no <see cref="AdSpot.PreviewPath"/> takes exactly today's path above — script check, then
    /// <see cref="IAdSpotStore.ApproveAsync"/>, nothing else (STORY-425 AC3). A row WITH a stamped
    /// preview gates on <see cref="PreviewIsStaleAsync"/> first (key mismatch OR the file itself gone —
    /// 409 <see cref="PreviewStaleType"/>, no write attempted, the row untouched) so a rejected approve
    /// never mutates the row it just refused. Once past that gate, the SAME
    /// <see cref="ValidateCurrentScriptThenAsync"/> call runs (script check + <c>ApproveAsync</c>,
    /// mapped exactly as the no-preview path above), then <see cref="IAdSpotStore.ClaimForPromotionAsync"/>
    /// claims the now-Approved row for THIS request specifically (Approved→Rendering, xmin-guarded
    /// against the row this request just read) before <see cref="AdRenderService.PromotePreviewAsync"/>
    /// moves the file and lands it as a real <c>library.media</c> row — see
    /// <see cref="AfterLandedAsync"/>/<see cref="AfterPromotionFailedAsync"/> for the two outcomes.
    /// </para>
    ///
    /// <para>
    /// <b>A lost claim is not this request's failure (STORY-425).</b> Between the
    /// <c>ApproveAsync</c> above and <see cref="IAdSpotStore.ClaimForPromotionAsync"/>, some other actor
    /// — the stuck-render guardian's own re-arm, or a genuinely concurrent second approve — can already
    /// have moved the row off <see cref="AdState.Approved"/>. <see cref="IAdSpotStore.ClaimForPromotionAsync"/>
    /// reports that as <see langword="null"/>, the same "re-read and see" contract every other xmin
    /// conflict on this row already gets; this one specific race answers 200 with the row's current
    /// state rather than a 409, since the approve itself already genuinely succeeded — only the
    /// promotion that would have followed it lost a race to something else that now owns the row.
    /// </para>
    /// </summary>
    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var (expectedVersion, ifMatchError) = ResolveIfMatch();
        if (ifMatchError is not null)
            return ifMatchError;

        var current = await spotStore.GetByIdAsync(id, ct);
        if (current is null)
            return NotFound();

        if (current.PreviewPath is null)
            return await ValidateCurrentScriptThenAsync(id, expectedVersion, spotStore.ApproveAsync, ct);

        if (await PreviewIsStaleAsync(current, ct))
            return Conflict(PreviewStaleProblem());

        var scriptResult = await ValidateCurrentScriptThenAsync(id, expectedVersion, spotStore.ApproveAsync, ct);
        if (scriptResult is not ObjectResult { StatusCode: StatusCodes.Status200OK })
            return scriptResult;

        var approved = await spotStore.GetByIdAsync(id, ct);
        if (approved is null)
            return NotFound();

        var claimed = await spotStore.ClaimForPromotionAsync(id, approved.Version, ct);
        if (claimed is null)
        {
            // Something else already moved this row off Approved between the ApproveAsync above and
            // this claim (the stuck-render guardian's own re-arm racing this exact call is the only
            // realistic source — see the method remarks). The approve itself already succeeded; only
            // the promotion that would have followed it lost the race. The worker/guardian owns the row
            // now — answer 200 with whatever it currently is, never a caller-facing failure.
            logger.LogInformation(
                "Ad spot {Id} approved, but its preview promotion was already claimed elsewhere", id);
            return await Success(approved, ct);
        }

        var outcome = await renderService.PromotePreviewAsync(claimed, ct);
        return outcome switch
        {
            AdPromotionOutcome.Landed => await AfterLandedAsync(id, ct),
            AdPromotionOutcome.Failed failed => await AfterPromotionFailedAsync(id, failed, ct),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
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
        return await MapTransition(outcome, ct);
    }

    // -----------------------------------------------------------------------
    // POST /api/ads/{id}/write — queue a write job (draft only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ads/{id}/write (SPEC F174.2, F174.3; STORY-422 AC1-AC3/AC7, STORY-423 AC1/AC3; PLAN
    /// T441) — queues an <see cref="AdScriptWriter"/> pass against the row's current
    /// <see cref="AdSpot.Brief"/> and sponsor. Legal only against <see cref="AdState.Draft"/> (409
    /// <c>ad_write_not_draft</c> otherwise — this controller's own check, see the class remarks: the
    /// store's own <see cref="AdSpotJobService.TryEnqueueAsync"/> guard is state-agnostic). An unknown
    /// id is 404; an already-claimed row is 409 <c>ad_job_busy</c>; a full station-wide queue is 429
    /// <c>ad_job_queue_full</c>. A successful enqueue answers 202 with the row's fresh state (a fresh
    /// read, since <see cref="AdSpotJobService.TryEnqueueAsync"/> already stamped it) — the write itself
    /// lands asynchronously.
    /// </summary>
    [HttpPost("{id:long}/write")]
    public async Task<IActionResult> Write(long id, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(id, ct);
        if (spot is null)
            return NotFound();
        if (spot.State != AdState.Draft)
            return Conflict(WriteNotDraftProblem());

        var enqueueResult = await jobService.TryEnqueueAsync(id, "write", ct);
        return await MapEnqueueResultAsync(id, enqueueResult, ct);
    }

    // -----------------------------------------------------------------------
    // DELETE /api/ads/{id}/job — cancel whatever is queued or running
    // -----------------------------------------------------------------------

    /// <summary>
    /// DELETE /api/ads/{id}/job (SPEC F174.2; STORY-422 AC6; PLAN T441) — cancels the id's own queued or
    /// running job (whichever it is) and clears its stamp, via
    /// <see cref="AdSpotJobService.CancelAsync"/>. Idempotent (PLAN T441 ruling): a row with no active
    /// job still answers 204, the same "clearing an already-clear job is a harmless no-op" posture
    /// <see cref="IAdSpotStore.ClearJobAsync"/> already holds. An unknown id is 404.
    /// </summary>
    [HttpDelete("{id:long}/job")]
    public async Task<IActionResult> DeleteJob(long id, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(id, ct);
        if (spot is null)
            return NotFound();

        await jobService.CancelAsync(id, ct);
        return NoContent();
    }

    // -----------------------------------------------------------------------
    // POST /api/ads/{id}/preview — queue a preview render (draft/approved only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/ads/{id}/preview (SPEC F174.4; STORY-424 AC1-AC5; PLAN T442) — queues a preview render
    /// of the row's CURRENT script/cast/bed against the live Ads render knobs, via the SAME
    /// <see cref="AdSpotJobService.TryEnqueueAsync"/>/<see cref="MapEnqueueResultAsync"/> plumbing
    /// <see cref="Write"/> uses (see the class remarks). An unknown id is 404. An empty/whitespace
    /// <see cref="AdSpot.Script"/> is 400 <c>script_required</c> (<c>field: script</c>) — a preview has
    /// nothing to render without one. A row outside <see cref="AdState.Draft"/>/<see cref="AdState.Approved"/>
    /// is 409 <c>ad_preview_not_editable</c>. Beyond that, enqueue answers exactly as <see cref="Write"/>'s
    /// own does: 409 <c>ad_job_busy</c> for an already-claimed row, 429 <c>ad_job_queue_full</c> for a
    /// full station-wide queue, 202 with the row's fresh state on a successful enqueue.
    /// </summary>
    [HttpPost("{id:long}/preview")]
    public async Task<IActionResult> Preview(long id, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(id, ct);
        if (spot is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(spot.Script))
            return BadRequest(RequiredFieldProblem("script", ScriptRequiredType));
        if (spot.State is not (AdState.Draft or AdState.Approved))
            return Conflict(PreviewNotEditableProblem());

        var enqueueResult = await jobService.TryEnqueueAsync(id, "preview", ct);
        return await MapEnqueueResultAsync(id, enqueueResult, ct);
    }

    // -----------------------------------------------------------------------
    // GET /api/ads/{id}/preview.wav — stream a rendered preview's own bytes
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /api/ads/{id}/preview.wav (SPEC F174.4; STORY-424 AC4; PLAN T442) — streams the row's most
    /// recently rendered preview clip. 404 covers every "nothing valid to serve" case alike, never
    /// distinguished in the response (the <see cref="MediaController.GetAudio"/> dead-file precedent
    /// one controller over): the id is unknown, no preview has ever been stamped
    /// (<see cref="AdSpot.PreviewPath"/> is <see langword="null"/>), the stamped
    /// <see cref="AdSpot.PreviewKey"/> no longer matches a fresh <see cref="AdPreviewKey.Compute"/>
    /// over the row's CURRENT inputs (stale — an edit since the last render), or the file itself is
    /// gone from disk.
    ///
    /// <para>
    /// <b>Path re-asserted under the canonical preview root, in THIS method (the CodeQL
    /// path-injection guard's own "strong guard" shape, <see cref="AdPreviewRoot.IsUnder"/>'s own
    /// remarks — the one construction/check site <see cref="AdRenderService"/> and the guardian share,
    /// PLAN T442 ruling).</b> Even though <c>preview_path</c> only ever reaches this row via
    /// <c>AdRenderService.RenderPreviewAsync</c>'s own write, a stored path is untrusted the instant it
    /// crosses a storage boundary; an escaped row answers 404 (never served) plus one Warning line
    /// naming only the spot id, never the raw path.
    /// </para>
    ///
    /// <para>
    /// <b>The unresolvable-sponsor 404 arm below is defensive-only, not reachable through this API.</b>
    /// <c>station.ad_spot.sponsor_id</c> carries <c>ON DELETE RESTRICT</c> (SPEC F171's own foreign
    /// key), so a sponsor can never be deleted while a spot still references it — the same guarantee
    /// <see cref="ToPreviewDto"/>'s own remarks lean on. A live spot's <c>sponsor</c> read can only come
    /// back <see langword="null"/> here if that database invariant itself were ever violated.
    /// </para>
    /// </summary>
    [HttpGet("{id:long}/preview.wav")]
    public async Task<IActionResult> PreviewWav(long id, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(id, ct);
        if (spot is not { PreviewPath: { } previewPath, PreviewKey: { } storedKey })
            return NotFound();

        var sponsor = await sponsorStore.GetAsync(spot.SponsorId, ct);
        if (sponsor is null)
            return NotFound();

        if (PreviewKeyIsStale(spot, storedKey, sponsor, AdLiveSettingsReader.Read(configuration)))
            return NotFound();

        var previewRoot = AdPreviewRoot.Resolve(locatorRoots);
        var target = Path.GetFullPath(previewPath);
        if (!AdPreviewRoot.IsUnder(previewRoot, target))
        {
            logger.LogWarning("Ad spot preview read found a preview path outside the preview root for spot {Id}", id);
            return NotFound();
        }

        if (!System.IO.File.Exists(target))
            return NotFound();

        return PhysicalFile(target, "audio/wav");
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps an <see cref="AdSpotJobEnqueueResult"/> to its response — the ONE switch both
    /// <see cref="Write"/> and <see cref="Preview"/> call (PLAN T442 ruling: two job kinds, one
    /// enqueue-outcome mapping, never a second hand-kept copy): <c>Busy</c> → 409
    /// <c>ad_job_busy</c>, <c>QueueFull</c> → 429 <c>ad_job_queue_full</c>, <c>NotFound</c> → 404,
    /// <c>Accepted</c> → 202 with a fresh read of the row (<see cref="AdSpotJobService.TryEnqueueAsync"/>
    /// already stamped it by the time this reads it back).
    /// </summary>
    async Task<IActionResult> MapEnqueueResultAsync(long id, AdSpotJobEnqueueResult enqueueResult, CancellationToken ct)
    {
        switch (enqueueResult)
        {
            case AdSpotJobEnqueueResult.Busy:
                return Conflict(JobBusyProblem());
            case AdSpotJobEnqueueResult.QueueFull:
                return StatusCode(StatusCodes.Status429TooManyRequests, JobQueueFullProblem());
            case AdSpotJobEnqueueResult.NotFound:
                return NotFound();
            case AdSpotJobEnqueueResult.Accepted:
                var fresh = await spotStore.GetByIdAsync(id, ct);
                if (fresh is null)
                    return NotFound();

                var sponsor = await sponsorStore.GetAsync(fresh.SponsorId, ct);
                Response.Headers.ETag = WeakETag.Format(fresh.Version);
                return Accepted(ToDto(fresh, sponsor, AdLiveSettingsReader.Read(configuration)));
            default:
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

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
    async Task<IActionResult> MapTransition(AdSpotTransitionOutcome outcome, CancellationToken ct) => outcome switch
    {
        { Result: AdSpotWriteResult.Updated, Spot: { } spot } => await Success(spot, ct),
        { Result: AdSpotWriteResult.NotFound } => NotFound(),
        { Result: AdSpotWriteResult.Conflict } => Conflict(ConflictProblem()),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };

    async Task<IActionResult> Success(AdSpot spot, CancellationToken ct)
    {
        var sponsor = await sponsorStore.GetAsync(spot.SponsorId, ct);
        Response.Headers.ETag = WeakETag.Format(spot.Version);
        return Ok(ToDto(spot, sponsor, AdLiveSettingsReader.Read(configuration)));
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

        // The owner-sponsor skips (SPEC F172.5, PLAN T438 ruling) need the row's CURRENT sponsor — a
        // null read here (the row's sponsor vanished between GetByIdAsync and this GetAsync;
        // ON DELETE RESTRICT makes it near-impossible, never truly impossible) falls through to
        // BuildValidationRequest's own strict default, never a skip on unresolvable data.
        var sponsor = await sponsorStore.GetAsync(current.SponsorId, ct);

        if (AdScriptValidator.Validate(
                current.Script ?? "", BuildValidationRequest(current.SpotSeconds, sponsor), durationEstimator)
            is AdScriptValidationResult.Refused refused)
        {
            return BadRequest(ScriptViolationProblem(refused.Violation));
        }

        return await MapTransition(await transition(id, expectedVersion, ct), ct);
    }

    /// <summary>
    /// <see cref="Approve"/>'s own pre-promotion staleness gate (SPEC F174.5; STORY-425 AC4; PLAN
    /// T445) — <see langword="true"/> when <paramref name="spot"/>'s stamped preview no longer matches
    /// what a fresh render of its CURRENT inputs would produce (<see cref="PreviewKeyIsStale"/>, the
    /// SAME comparison <see cref="ToPreviewDto"/> runs on every read) OR the file it points at is
    /// simply gone. Only ever called once <see cref="Approve"/> has already confirmed
    /// <paramref name="spot"/> carries a preview — an unstamped row never reaches here.
    ///
    /// <para>
    /// <b>Path re-asserted under the canonical preview root, IN THIS METHOD (the CodeQL
    /// path-injection guard's own "strong guard" shape — <see cref="AdPreviewRoot.IsUnder"/>'s own
    /// remarks, the SAME posture <see cref="PreviewWav"/> and
    /// <see cref="AdRenderService.PromotePreviewAsync"/> each hold independently rather than sharing a
    /// filesystem-touching helper).</b> A stored path is untrusted the instant it crosses a storage
    /// boundary, regardless of how many other call sites already trust it.
    /// </para>
    /// </summary>
    async Task<bool> PreviewIsStaleAsync(AdSpot spot, CancellationToken ct)
    {
        if (spot is not { PreviewPath: { } previewPath, PreviewKey: { } previewKey })
            return true;

        var sponsor = await sponsorStore.GetAsync(spot.SponsorId, ct);
        var liveSettings = AdLiveSettingsReader.Read(configuration);
        if (PreviewKeyIsStale(spot, previewKey, sponsor, liveSettings))
            return true;

        var previewRoot = AdPreviewRoot.Resolve(locatorRoots);
        var target = Path.GetFullPath(previewPath);
        if (!AdPreviewRoot.IsUnder(previewRoot, target))
        {
            logger.LogWarning(
                "Ad spot approve found a preview path outside the preview root for spot {Id}", spot.Id);
            return true;
        }

        return !System.IO.File.Exists(target);
    }

    /// <summary>
    /// <see cref="Approve"/>'s own success path once <see cref="AdRenderService.PromotePreviewAsync"/>
    /// answers <see cref="AdPromotionOutcome.Landed"/> (STORY-425 AC1, AC2; PLAN T445) — the preview
    /// stamp is spent (<see cref="IAdSpotStore.ClearPreviewAsync"/>: the file it named is gone, moved
    /// onto the landed <c>library.media</c> row itself), then the row is re-read fresh so the response
    /// carries the real post-landing state (<c>ready</c>, <c>mediaId</c> set, <c>preview: null</c>) —
    /// the promotion already stamped these via <see cref="IAdSpotStore.MarkReadyAsync"/> inside
    /// <see cref="AdRenderService.PromotePreviewAsync"/> itself, this method's own read simply observes
    /// it.
    /// </summary>
    async Task<IActionResult> AfterLandedAsync(long id, CancellationToken ct)
    {
        await spotStore.ClearPreviewAsync(id, ct);
        var landed = await spotStore.GetByIdAsync(id, ct);
        return landed is null ? NotFound() : await Success(landed, ct);
    }

    /// <summary>
    /// <see cref="Approve"/>'s own failure path once <see cref="AdRenderService.PromotePreviewAsync"/>
    /// answers <see cref="AdPromotionOutcome.Failed"/> (STORY-425 AC5; PLAN T445) —
    /// <see cref="IAdSpotStore.ReArmAsync"/> returns the row from <see cref="AdState.Rendering"/> to
    /// <see cref="AdState.Approved"/> (never <see cref="AdState.Failed"/> — see
    /// <see cref="AdRenderService.PromotePreviewAsync"/>'s own remarks for why), so the operator can
    /// simply approve again once whatever blocked the move is fixed. <paramref name="failed"/>'s own
    /// <see cref="AdPromotionOutcome.Failed.FileMoved"/> decides the preview stamp's fate: if the move
    /// itself never happened, the stamp still names a real file — left exactly as it was, so a bare
    /// retry can promote it without re-rendering; if the file is already gone (moved, then something
    /// downstream failed), the stamp now points at nothing and is cleared. This controller performs no
    /// filesystem access of its own here — both facts come from <paramref name="failed"/> alone.
    /// </summary>
    async Task<IActionResult> AfterPromotionFailedAsync(long id, AdPromotionOutcome.Failed failed, CancellationToken ct)
    {
        await spotStore.ReArmAsync(id, ct);
        if (failed.FileMoved)
            await spotStore.ClearPreviewAsync(id, ct);

        logger.LogWarning("Ad spot {Id} preview promotion failed: {Reason}", id, LogSanitize.Strip(failed.Reason));
        return Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            type: PreviewPromotionFailedType,
            detail: "Promoting this spot's approved preview to on-air failed. The spot is back to Approved — try approving again.");
    }

    /// <summary>Builds the <see cref="AdScriptValidator"/> request for one script check, carrying the
    /// owner-sponsor skips (SPEC F172.5) when <paramref name="sponsor"/> is a real, non-pack-owned row:
    /// a <see langword="null"/> <paramref name="sponsor"/> (the row's sponsor could not be resolved)
    /// falls through to <see cref="AdScriptValidationRequest"/>'s own fail-closed defaults — no name,
    /// no phone, <c>IsPackOwned: true</c> — the same strict posture a pack sponsor gets.</summary>
    AdScriptValidationRequest BuildValidationRequest(int spotSeconds, Sponsor? sponsor) => new(
        audiencePosture.Current, copyBounds.MaxCopyChars, spotSeconds, adsOptions.CurrentValue.DurationToleranceRatio,
        SponsorName: sponsor?.Name, SponsorPhone: sponsor?.Phone, IsPackOwned: sponsor is null || sponsor.PackSlug is not null);

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

    static SponsorRefDto SponsorRefFor(long sponsorId, Sponsor? sponsor) =>
        sponsor is not null ? SponsorRefDto.From(sponsor) : new SponsorRefDto(sponsorId, string.Empty, false);

    static string? SerializeVoicePlan(IReadOnlyList<AdVoicePlanEntry>? plan) =>
        plan is null or { Count: 0 } ? null : AdVoicePlanJson.Serialize(plan);

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
            return JsonSerializer.Deserialize<IReadOnlyList<AdVoicePlanEntry>>(json, AdVoicePlanJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Projects one row to its wire shape — an INSTANCE method (PLAN T441), not
    /// <see langword="static"/> like every other pure helper below it, because <see cref="AdSpotJobDto"/>
    /// needs <see cref="jobService"/>'s own in-memory <see cref="AdSpotJobService.IsWaitingForStation"/>
    /// read (PLAN T441 ruling: <c>job: null</c> exactly when the row carries neither
    /// <see cref="AdSpot.JobKind"/> nor <see cref="AdSpot.JobError"/>, otherwise the object — so a
    /// failed job's error stays visible with <c>kind</c> null). <paramref name="sponsor"/> is the FULL
    /// row (a single <see cref="ISponsorStore.GetAsync"/> call at every single-row call site, or
    /// <see cref="List"/>'s own page-wide dictionary), not merely its wire cross-reference —
    /// <see cref="ToPreviewDto"/> needs it to recompute the staleness key. <paramref name="liveSettings"/>
    /// is read ONCE by the caller (PLAN T442 ruling) — never re-read here per row; see
    /// <see cref="List"/>'s own remarks for why that hoist matters.</summary>
    AdSpotDto ToDto(AdSpot spot, Sponsor? sponsor, AdLiveSettings liveSettings) => new(
        spot.Id, spot.SponsorId, spot.SponsorName, SponsorRefFor(spot.SponsorId, sponsor), spot.Title, spot.Brief,
        spot.Script, AdSourceTokens.ToToken(spot.Source), spot.PackSlug, spot.SpotSeconds,
        DeserializeVoicePlan(spot.VoicePlan), spot.BedMediaId, AdStateTokens.ToToken(spot.State), spot.FailReason,
        spot.MediaId, spot.CreatedAt, spot.StateChangedAt, spot.RenderedAt, spot.RetiredAt, spot.Version,
        ToJobDto(spot), ToPreviewDto(spot, sponsor, liveSettings));

    AdSpotJobDto? ToJobDto(AdSpot spot) => spot is { JobKind: null, JobError: null }
        ? null
        : new AdSpotJobDto(spot.JobKind, spot.JobStartedAt, jobService.IsWaitingForStation(spot.Id), spot.JobError);

    /// <summary>
    /// Projects a spot's own preview stamp to its wire shape (SPEC F174.4; STORY-424 AC4; PLAN T442) —
    /// <see langword="null"/> exactly when <see cref="AdSpot.PreviewPath"/> is <see langword="null"/>
    /// (no preview has ever been rendered for this row). "Stale" is never a stored flag (PLAN T442
    /// ruling): it is the live comparison of the stored <see cref="AdSpot.PreviewKey"/> against a
    /// freshly recomputed <see cref="AdPreviewKey.Compute"/> over the row's CURRENT inputs, so an edit
    /// to the script/cast/bed, or an operator changing a Live Ads setting, makes an existing preview
    /// read stale on the very next GET — no background job ever has to notice and flip anything.
    /// <paramref name="liveSettings"/> is <see cref="ToDto"/>'s own caller-supplied read, never a fresh
    /// <see cref="AdLiveSettingsReader.Read"/> per row here — <see cref="List"/> builds one page of N
    /// DTOs from a SINGLE Live-settings read; the SHA256 digest in <see cref="AdPreviewKey.Compute"/>
    /// below still runs once PER ROW regardless (db/46's own ruling: staleness is derived, never a
    /// stored flag, so that part of the per-row cost is intentional, not this method's own waste). A
    /// missing <paramref name="sponsor"/> (the defensive-only <c>ON DELETE RESTRICT</c> gap —
    /// <see cref="PreviewWav"/>'s own remarks) has nothing to recompute the key against, so it reads as
    /// stale rather than trusting a key nothing can verify.
    /// </summary>
    AdSpotPreviewDto? ToPreviewDto(AdSpot spot, Sponsor? sponsor, AdLiveSettings liveSettings)
    {
        if (spot is not { PreviewPath: not null, PreviewAt: { } at, PreviewKey: { } key })
            return null;

        return new AdSpotPreviewDto(at, key, Stale: PreviewKeyIsStale(spot, key, sponsor, liveSettings));
    }

    /// <summary>
    /// The key-comparison half of "is this spot's stamped preview stale" (SPEC F174.4/F174.5; PLAN
    /// T442, T445) — the ONE live copy <see cref="ToPreviewDto"/> (every read), <see cref="PreviewWav"/>
    /// (once past its own "no preview stamped" and "unresolvable sponsor" guards) and
    /// <see cref="PreviewIsStaleAsync"/> (<see cref="Approve"/>'s own pre-promotion gate) all call,
    /// per the brief's "shared helper, one live copy" ruling. The file-existence/path-jail half of
    /// staleness stays duplicated by design, inline in each of <see cref="PreviewWav"/> and
    /// <see cref="PreviewIsStaleAsync"/> (the CodeQL path-injection guard's own "strong guard" posture —
    /// the check belongs in the SAME method as the file access it guards, never behind a shared
    /// filesystem-touching helper another method calls). A missing <paramref name="sponsor"/> (the
    /// defensive-only <c>ON DELETE RESTRICT</c> gap — <see cref="PreviewWav"/>'s own remarks) has
    /// nothing to recompute the key against, so it reads as stale rather than trusting a key nothing can
    /// verify.
    /// </summary>
    bool PreviewKeyIsStale(AdSpot spot, string storedKey, Sponsor? sponsor, AdLiveSettings liveSettings) =>
        sponsor is null ||
        storedKey != AdPreviewKey.Compute(spot, sponsor, liveSettings, adsOptions.CurrentValue.BedDuckDb);

    /// <summary>
    /// PLAN T403 carry-forward (b), now via the shared <see cref="WeakETag.TryParseVersion"/>:
    /// validates the <c>If-Match</c> token BEFORE it ever reaches
    /// <c>@expectedVersion::xid</c> — absent → 428 (<c>MediaController.Patch</c>'s own precedent);
    /// present but not a well-formed <c>xid</c> (Postgres's own 32-bit unsigned domain) → 400, never a
    /// raw <see cref="Npgsql.PostgresException"/> 22P02 the way an unvalidated token reaching the SQL
    /// cast would produce. The <c>(T?, IActionResult?)</c> tuple shape (CONTRIBUTING.md's
    /// no-null-forgiving-operator rule) mirrors <c>ResolveBedMediaIdAsync</c> one
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
    /// <b>Carry-forward (T403 review finding 9) RESOLVED for Ads and Sponsors, still open for Media.</b>
    /// This method and <see cref="SponsorsController.ResolveIfMatch"/> both now call
    /// <see cref="WeakETag.TryParseVersion"/> — the single shared strip/validate implementation T403's
    /// own review finding 9 called for. <c>MediaController.Patch</c> is the one surface NOT migrated:
    /// its own <c>StripETagWrapper</c> still returns a raw, unvalidated token straight through to
    /// <c>@expectedVersion::xid</c>, so a malformed <c>If-Match</c> against <c>PATCH /api/media/{id}</c>
    /// still produces a raw <see cref="Npgsql.PostgresException"/> (SqlState 22P02) rather than a clean
    /// 400 — see <see cref="WeakETag"/>'s own remarks for why that migration is a behaviour change left
    /// outside this task.
    /// </para>
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

    /// <summary>The <see cref="AdBriefsController.RequiredFieldProblem"/> precedent: <paramref name="type"/>
    /// lets a caller (only <see cref="Create"/>'s <c>sponsorId</c> check, so far) attach a distinct
    /// <c>ProblemDetails.Type</c> a client can branch on; every other required-field 400 leaves it
    /// <see langword="null"/>, same as before this parameter existed.</summary>
    static ProblemDetails RequiredFieldProblem(string field, string? type = null)
    {
        // ProblemDetails is an ordinary class, not a record — no `with` expression available — so the
        // de-duplication is a mutate-then-return of FieldProblem's own result rather than a copy
        // expression.
        var problem = FieldProblem(field, "is required.");
        problem.Type = type;
        return problem;
    }

    static ProblemDetails SponsorNotFoundProblem(long sponsorId) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Type   = SponsorNotFoundType,
        Detail = $"No sponsor with id {sponsorId} exists.",
        Extensions = { ["field"] = "sponsorId" },
    };

    static ProblemDetails SponsorPausedProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = SponsorPausedType,
        Detail = "This sponsor is paused. Resume it before creating a spot.",
        Extensions = { ["field"] = "sponsorId" },
    };

    static ProblemDetails InvalidSponsorIdQueryProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "sponsorId must be a whole number.",
        Extensions = { ["field"] = "sponsorId" },
    };

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

    static ProblemDetails WriteNotDraftProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = WriteNotDraftType,
        Detail = "Only a draft spot can be written. Re-fetch and retry if this spot has since moved on.",
    };

    static ProblemDetails PreviewNotEditableProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = PreviewNotEditableType,
        Detail = "Only a draft or approved spot can be previewed. Re-fetch and retry if this spot has since moved on.",
    };

    static ProblemDetails PreviewStaleProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = PreviewStaleType,
        Detail = "The preview no longer matches this spot. Render it again, then approve.",
    };

    static ProblemDetails JobBusyProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Conflict.",
        Type   = JobBusyType,
        Detail = "A job is already queued or running for this spot.",
    };

    static ProblemDetails JobQueueFullProblem() => new()
    {
        Status = StatusCodes.Status429TooManyRequests,
        Title  = "Too many requests.",
        Type   = JobQueueFullType,
        Detail = "The station-wide job queue is full. Try again shortly.",
    };
}
