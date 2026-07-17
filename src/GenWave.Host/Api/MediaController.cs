using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Media catalog endpoints for the Admin UI. Scope is the single station's configured library
/// scope, read live via <see cref="IStationScopeProvider"/> on every call (SPEC F30.1) — no
/// tenancy/X-Station-Id.
///
/// Separate from the anonymous <see cref="MediaEndpoints"/> minimal-API group, which serves the
/// Liquidsoap/Orchestrator hot path on <c>/media/*</c>. These endpoints live under <c>/api/media</c>
/// so they participate in cookie auth and the (config-gated) deny-by-default policy.
/// </summary>
[ApiController]
[Route("api")]
public sealed class MediaController(
    IAdminMediaQuery adminQuery,
    IAdminMediaLookup adminLookup,
    IAdminMediaWrite adminWrite,
    IStationScopeProvider scopeProvider,
    ILogger<MediaController> logger) : ControllerBase
{
    /// <summary>
    /// GET /api/media — paged, filtered, station-scoped media list.
    /// Query parameters:
    ///   state        — exact state match (e.g. "ready", "failed", "discovered")
    ///   artist       — case-insensitive substring match on artist column
    ///   genre        — case-insensitive substring match on genre column
    ///   library-id   — narrow to a single library (must be within station scope; returns [] if not)
    ///   q            — substring search across title, artist, album
    ///   eligible     — true|false filter; absent means all rows regardless of eligibility
    ///   never-play   — true narrows to flagged rows (SPEC F33.10); absent or false applies no
    ///                  filter — a track must stay findable after it leaves the play-history ring,
    ///                  so no "only unflagged" mode is offered here
    ///   year         — exact release-year match (SPEC F49.1)
    ///   decade       — a decade start year (e.g. 1970 means <c>year BETWEEN 1970 AND 1979</c>);
    ///                  a value not divisible by 10 → 400 (SPEC F49.1)
    ///   year-missing — true narrows to rows with no release year (<c>year IS NULL</c>); absent or
    ///                  false applies no filter — mirrors never-play's strictness (SPEC F49.1)
    ///   artist-exact — case-insensitive EQUALITY match on artist (SPEC F52.3); naming this
    ///                  alongside <c>artist</c> → 400 (mutual exclusion, the F49.1 precedent)
    ///   album-exact  — case-insensitive EQUALITY match on album (SPEC F52.3); no substring
    ///                  counterpart exists for album, so this never conflicts with anything —
    ///                  <c>q</c> still searches album and remains combinable
    ///   genre-exact  — repeatable; case-insensitive EQUALITY match, OR'd across every occurrence
    ///                  (SPEC F52.3); naming this alongside <c>genre</c> → 400 (mutual exclusion)
    ///   page         — 1-based page number (default 1)
    ///   limit        — items per page, clamped to [1, 200] (default 50)
    ///
    /// Naming more than one of <c>year</c>/<c>decade</c>/<c>year-missing=true</c> together → 400
    /// (SPEC F49.1) — each narrows by release year in an incompatible way, so at most one applies.
    ///
    /// Every row carries camelCase <c>score</c>/<c>neverPlay</c> resolved via a LEFT JOIN +
    /// COALESCE against <c>library.media_rating</c> (SPEC F33.10); an unrated row reads the F33.2
    /// ledger default (score 50, not flagged). Every row also carries <c>bpm</c>/<c>trackEnergy</c>
    /// (SPEC F49.2), null until analyzed/measured.
    ///
    /// Response headers:
    ///   X-Pagination: total={n},pages={n},page={n},limit={n}
    /// </summary>
    [HttpGet("media")]
    public async Task<IActionResult> List(
        [FromQuery] string? state,
        [FromQuery] string? artist,
        [FromQuery] string? genre,
        [FromQuery(Name = "library-id")] long? libraryId,
        [FromQuery] string? q,
        [FromQuery] bool? eligible,
        [FromQuery(Name = "never-play")] bool? neverPlay = null,
        [FromQuery] int? year = null,
        [FromQuery] int? decade = null,
        [FromQuery(Name = "year-missing")] bool? yearMissing = null,
        [FromQuery(Name = "artist-exact")] string? artistExact = null,
        [FromQuery(Name = "album-exact")] string? albumExact = null,
        [FromQuery(Name = "genre-exact")] string[]? genreExact = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        page  = Math.Max(1, page);

        // SPEC F49.1: at most one of year/decade/year-missing=true may be named — each narrows by
        // release year in a mutually incompatible way. Mirrors never-play: only true counts as
        // "named" for year-missing; absent/false is "no filter", not a conflict participant.
        var namedYearFilters = (year.HasValue ? 1 : 0) + (decade.HasValue ? 1 : 0) + (yearMissing is true ? 1 : 0);
        if (namedYearFilters > 1)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Conflicting year filters.",
                Detail = "Name at most one of year, decade, or year-missing=true.",
            });
        }

        // SPEC F49.1: decade must be a decade-aligned start year (divisible by 10) — 1975 doesn't
        // mean anything as a BETWEEN start.
        if (decade.HasValue && decade.Value % 10 != 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid decade.",
                Detail = $"decade must be a decade-aligned year (e.g. 1970, 1980); got {decade.Value}.",
            });
        }

        // SPEC F52.3: a field's substring and exact params are mutually exclusive — the F49.1
        // year/decade precedent. album has no substring counterpart, so no check is needed there.
        if (artist is not null && artistExact is not null)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Conflicting artist filters.",
                Detail = "Name at most one of artist or artist-exact.",
            });
        }

        if (genre is not null && genreExact is { Length: > 0 })
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Conflicting genre filters.",
                Detail = "Name at most one of genre or genre-exact.",
            });
        }

        // Resolve the effective scope: a named library-id overrides the station rotation scope
        // (F23.2 / STORY-064). An unnamed browse uses the station scope as before. An out-of-scope
        // browse is flagged with X-Out-Of-Scope: true so the UI can surface a banner; rows are
        // returned regardless — scope is a curation boundary, not a trust boundary (F23.6).
        // scopeProvider.Current is read fresh on every call (SPEC F30.1).
        var (effectiveScope, outOfScope) = EffectiveScope.Resolve(scopeProvider.Current, libraryId);

        if (outOfScope)
            Response.Headers["X-Out-Of-Scope"] = "true";

        var query = new MediaQuery(
            state, artist, genre, libraryId, q, page, limit, eligible, neverPlay,
            year, decade, yearMissing, artistExact, albumExact, genreExact);
        var result = await adminQuery.ListAdminAsync(effectiveScope, query, ct);

        Response.Headers["X-Pagination"] =
            $"total={result.Total},pages={result.Pages},page={page},limit={limit}";

        return Ok(result.Items);
    }

    /// <summary>
    /// POST /api/media/bulk/reassign — bulk-reassign every matching in-scope row to a new library (L4).
    ///
    /// Security contract:
    ///   • Requires cookie auth (deny-by-default policy when Admin:Password is set).
    ///   • Requires Content-Type: application/json — rejects other types with 415 (CSRF guard).
    ///   • <c>toLibraryId</c> is required; absent or null → 400.
    ///   • <c>toLibraryId</c> unknown (no library.library row) → 400, nothing written.
    ///   • Empty filter → matches every in-scope source row (AC7 — no implicit refusal).
    ///   • Filter library-id narrowing is intersected with station scope before reaching the repo.
    ///   • Empty station scope → 0 updated (default-deny — never a full-table update).
    ///   • Destination in scope  → 200 { updated: N }.
    ///   • Destination out of scope → 200 { updated: N, outOfScope: true } + X-Out-Of-Scope: true header.
    ///   • Parameterized WHERE only — no filter value concatenated into SQL (same hygiene as F3).
    /// </summary>
    [HttpPost("media/bulk/reassign")]
    [Consumes("application/json")]
    public async Task<IActionResult> BulkReassign(
        [FromBody] BulkReassignRequest request,
        CancellationToken ct)
    {
        // AC6: toLibraryId is required. Since it is long? in the DTO, a missing JSON field or
        // explicit null both arrive as null here — return 400 before touching the repo.
        if (request.ToLibraryId is null)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "toLibraryId is required.",
                Detail = "The request body must include a numeric toLibraryId field.",
            });
        }

        var toLibraryId = request.ToLibraryId.Value;

        // Keep a reference to the station scope — used for the OutOfScopeWarning check below.
        // Read fresh on every call (SPEC F30.1).
        var stationScope = scopeProvider.Current;
        var filter       = request.Filter;

        // Named library-id overrides the station rotation scope (F23.3 / STORY-065): the named
        // library becomes the effective scope whether or not it falls inside the station rotation.
        // An unnamed filter stays bounded by the station scope.
        var (scope, _) = EffectiveScope.Resolve(stationScope, filter?.LibraryId);

        var mediaQuery = new MediaQuery(
            State:       filter?.State,
            Artist:      filter?.Artist,
            Genre:       filter?.Genre,
            LibraryId:   filter?.LibraryId,
            Q:           filter?.Q,
            ArtistExact: filter?.ArtistExact,
            AlbumExact:  filter?.AlbumExact,
            GenresExact: filter?.GenresExact);

        // null → toLibraryId not found in library.library; 0..N → rows updated.
        var updated = await adminWrite.BulkReassignAsync(mediaQuery, toLibraryId, scope, ct);

        // AC5: unknown toLibraryId → 400. The repo returns null to distinguish "library not found"
        // from "zero rows matched the filter" (which would be int 0).
        if (updated is null)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Unknown library.",
                Detail = $"No library with id {toLibraryId} exists.",
            });
        }

        logger.LogInformation(
            "BulkReassign toLibraryId={ToLibraryId} filter={@Filter} updated={Updated}",
            toLibraryId, filter, updated.Value);

        // AC3: destination out of scope → set X-Out-Of-Scope header and include outOfScope body field.
        var outOfScope = OutOfScopeWarning.ApplyIfOutOfScope(Response, toLibraryId, stationScope);

        return outOfScope
            ? Ok(new { updated = updated.Value, outOfScope = true })
            : Ok(new { updated = updated.Value });
    }

    /// <summary>
    /// POST /api/media/eligibility — bulk-set eligible on every row matching the given filter
    /// within the station's library scope (F3).
    ///
    /// Security contract:
    ///   • Requires cookie auth (deny-by-default policy when Admin:Password is set).
    ///   • Requires Content-Type: application/json — rejects other types with 415 (CSRF guard).
    ///   • Empty station scope → 0 affected (default-deny — never a full-table update).
    ///   • library-id filter is intersected with station scope before reaching the repository.
    ///
    /// Returns: 200 { affected: &lt;int&gt; }
    /// </summary>
    [HttpPost("media/eligibility")]
    [Consumes("application/json")]
    public async Task<IActionResult> BulkSetEligibility(
        [FromBody] BulkEligibilityRequest request,
        CancellationToken ct)
    {
        // Named library-id overrides the station rotation scope (F23.3 / STORY-065): converged on
        // the shared EffectiveScope.Resolve helper; behaviour for the unnamed case is unchanged.
        var (scope, _) = EffectiveScope.Resolve(scopeProvider.Current, request.Filter.LibraryId);

        // Map the request filter to the shared MediaQuery type so SetEligibilityAsync reuses
        // the exact same WHERE-clause builder as ListAdminAsync.
        var filter = new MediaQuery(
            State:       request.Filter.State,
            Artist:      request.Filter.Artist,
            Genre:       request.Filter.Genre,
            LibraryId:   request.Filter.LibraryId,
            Q:           request.Filter.Q,
            Eligible:    request.Filter.Eligible,
            ArtistExact: request.Filter.ArtistExact,
            AlbumExact:  request.Filter.AlbumExact,
            GenresExact: request.Filter.GenresExact);

        var affected = await adminWrite.SetEligibilityAsync(filter, request.Eligible, scope, ct);

        logger.LogInformation(
            "BulkSetEligibility eligible={Eligible} filter={@Filter} affected={Affected}",
            request.Eligible, request.Filter, affected);

        return Ok(new { affected });
    }

    /// <summary>
    /// GET /api/media/{id} — single media row, reachable by id regardless of station scope
    /// (SPEC F43.1, closes gitea-#203: scope is a curation filter, not an access gate).
    ///
    /// Security contract (IDOR-safe, security-api hard rule #1):
    ///   • Row does not exist              → 404 (no data in response or logs)
    ///   • Row exists                      → 200 with full enrichment columns
    ///   • Row's library not in station scope → 200 additionally carries
    ///     <c>X-Out-Of-Scope: true</c> (reuses <see cref="OutOfScopeWarning"/>, the same signal
    ///     already used for PATCH's destination-reassign warning, F20.6)
    ///
    /// The response includes a weak ETag (<c>W/"&lt;xmin&gt;"</c>) derived from the row's Postgres
    /// <c>xmin</c> system column for use with PATCH /api/media/{id} (W2 optimistic concurrency).
    /// The payload also carries camelCase <c>score</c>/<c>neverPlay</c> (SPEC F33.10); since rating
    /// writes never touch <c>library.media</c>'s <c>xmin</c> (F33.1), the ETag is unaffected by any
    /// number of votes or never-play toggles on this row.
    /// </summary>
    [HttpGet("media/{id:long}")]
    public async Task<IActionResult> GetById(
        long id,
        CancellationToken ct)
    {
        // Existence-first lookup — the IDOR-safe pattern: an unknown id is 404 before anything
        // else is evaluated. Scope no longer decides reachability (F43.1); it only decides
        // whether the X-Out-Of-Scope banner header is added below.
        var found = await adminLookup.GetByIdWithLibraryAsync(id, ct);

        if (found is null)
            return NotFound();

        var (row, libraryId) = found.Value;

        OutOfScopeWarning.ApplyIfOutOfScope(Response, libraryId, scopeProvider.Current);

        // Weak ETag from the row's xmin — allows the client to supply If-Match on PATCH.
        Response.Headers.ETag = FormatWeakETag(row.Version);

        return Ok(row);
    }

    /// <summary>
    /// PATCH /api/media/{id} — sparse tag + eligibility update with optimistic concurrency (W2).
    /// Also supports library reassignment via the <c>libraryId</c> body field (STORY-048, Epic J).
    ///
    /// Security contract:
    ///   • Requires cookie auth (covered by deny-by-default policy when Admin:Password is set).
    ///   • Requires <c>Content-Type: application/json</c> — rejects other types with 415 (CSRF guard).
    ///   • Requires <c>If-Match</c> header containing the weak ETag returned by GET — 428 if absent.
    ///   • <c>If-Match</c> value mismatch → 409 Conflict (another write occurred).
    ///   • Any existing row is reachable regardless of station scope (SPEC F43.2, closes gitea-#203) —
    ///     the source-row scope check is repealed; see the response shape note below.
    ///   • Unknown id → 404.
    ///   • Unknown libraryId (references no library row) → 400.
    ///   • All errors: ProblemDetails, no SQL or stack traces in the body.
    ///
    /// Response shape:
    ///   • No <c>libraryId</c> in the request → 204 No Content (unchanged F18 behaviour), plus
    ///     <c>X-Out-Of-Scope: true</c> when the row's (unchanged) library is out of station scope
    ///     (SPEC F43.2 — reuses <see cref="OutOfScopeWarning"/>).
    ///   • <c>libraryId</c> present, destination in scope     → 200, no <c>X-Out-Of-Scope</c> header, no <c>outOfScope</c> body field.
    ///   • <c>libraryId</c> present, destination out of scope → 200, <c>X-Out-Of-Scope: true</c> header, body <c>{ outOfScope: true }</c> (F20.6, unchanged).
    ///
    /// Only non-null fields in the body are written; absent fields are left unchanged. When any tag
    /// field (title/artist/album/genre/year) is present, <c>tags_edited_at</c> is stamped to now()
    /// — the W3 sentinel preventing re-enrichment from clobbering operator edits.
    ///
    /// Every successful response (204, and both 200 reassign variants) carries a fresh
    /// <c>ETag: W/"&lt;new xmin&gt;"</c>, read straight from the UPDATE's <c>RETURNING</c> clause —
    /// no second read (STORY-103). Failure responses (400/404/409/428) never carry an ETag.
    /// </summary>
    [HttpPatch("media/{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Patch(
        long id,
        [FromBody] MediaPatch patch,
        CancellationToken ct)
    {
        // If-Match is required — 428 Precondition Required if absent.
        var ifMatch = Request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired,
                new ProblemDetails
                {
                    Status = StatusCodes.Status428PreconditionRequired,
                    Title  = "If-Match required.",
                    Detail = "Include the ETag from GET /api/media/{id} as the If-Match header value.",
                });
        }

        // Strip the weak ETag wrapper (W/"<xmin>") to get the raw xmin token.
        var expectedVersion = StripETagWrapper(ifMatch);

        var scope   = scopeProvider.Current;
        var outcome = await adminWrite.UpdateReturningVersionAsync(
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            patch,
            expectedVersion,
            scope,
            ct);

        return outcome.Result switch
        {
            MediaWriteResult.Updated         => BuildPatchSuccess(patch, scope, outcome.NewVersion, outcome.LibraryId),
            MediaWriteResult.UnknownLibraryId => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Unknown library.",
                Detail = $"No library with id {patch.LibraryId} exists.",
            }),
            MediaWriteResult.Conflict        => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title  = "Conflict.",
                Detail = "The row was modified since you last read it. Re-fetch and retry.",
            }),
            MediaWriteResult.NotFound        => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Builds the success response for a successful PATCH, stamping the fresh <c>ETag</c> from
    /// <paramref name="newVersion"/> (STORY-103) before choosing the body shape.
    ///
    /// When no <c>libraryId</c> was requested: 204 No Content (unchanged F18 behaviour). The row's
    /// library is unchanged by this write, so <paramref name="rowLibraryId"/> — the row's current
    /// library, read straight from the same <c>RETURNING</c>/no-op SELECT that produced
    /// <paramref name="newVersion"/>, no second read — decides whether the
    /// <c>X-Out-Of-Scope: true</c> header is added (SPEC F43.2).
    ///
    /// When <c>libraryId</c> was requested, the destination is already known from the request body:
    ///   • destination in scope  → 200 with empty body (no <c>outOfScope</c> field — AC2).
    ///   • destination out of scope → 200 with <c>{ outOfScope: true }</c> + <c>X-Out-Of-Scope: true</c> header (F20.6, AC3).
    /// </summary>
    IActionResult BuildPatchSuccess(MediaPatch patch, LibraryScope scope, string? newVersion, long? rowLibraryId)
    {
        // newVersion is always populated when the write outcome is Updated; the null check is
        // defense-in-depth only — a successful write must never be left without a fresh ETag to
        // report, but it also must never fabricate one.
        if (newVersion is not null)
            Response.Headers.ETag = FormatWeakETag(newVersion);

        if (patch.LibraryId.HasValue)
        {
            var outOfScope = OutOfScopeWarning.ApplyIfOutOfScope(Response, patch.LibraryId.Value, scope);
            return outOfScope
                ? Ok(new { outOfScope = true })
                : Ok(new { });
        }

        // No reassignment requested — the row's library is unchanged by this write (F43.2).
        // rowLibraryId is always populated alongside newVersion on an Updated outcome; the null
        // check is defense-in-depth only, mirroring the newVersion guard above.
        if (rowLibraryId.HasValue)
            OutOfScopeWarning.ApplyIfOutOfScope(Response, rowLibraryId.Value, scope);

        return NoContent();
    }

    /// <summary>
    /// Strips the weak ETag wrapper so the raw xmin token can be passed to the repository.
    /// Accepts <c>W/"&lt;token&gt;"</c> (RFC 7232 weak) or plain <c>"&lt;token&gt;"</c>.
    /// Returns the input unchanged if neither wrapper is present (graceful: the UPDATE will
    /// just fail the xmin cast and produce a Conflict / NotFound, never a crash).
    /// </summary>
    static string StripETagWrapper(string etag)
    {
        var tag = etag.Trim();
        if (tag.StartsWith("W/\"", StringComparison.Ordinal) && tag.EndsWith('"'))
            return tag[3..^1];
        if (tag.StartsWith('"') && tag.EndsWith('"'))
            return tag[1..^1];
        return tag;
    }

    /// <summary>
    /// Formats a row's <c>xmin</c> version token as a weak ETag (<c>W/"&lt;version&gt;"</c> per
    /// RFC 7232 §2.3) — the single place this string is built, shared by <see cref="GetById"/> and
    /// <see cref="Patch"/> so the two never drift (STORY-103).
    /// </summary>
    static string FormatWeakETag(string version) => $"W/\"{version}\"";
}
