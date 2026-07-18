using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Re-enrichment scheduling endpoints (Epic J, STORY-051 — SPEC F20).
///
/// <para>
/// Kept separate from <see cref="MediaController"/> to decouple the write path from re-enrichment
/// concerns. Test doubles for the write surface do not need to implement schedule methods.
/// </para>
///
/// <para>
/// <b>Mechanism:</b> each endpoint issues a single transactional UPDATE that sentinel-resets the
/// column group(s) selected by the <c>fields</c> parameter. The existing enricher worker reclaims
/// affected rows via its shipped backfill predicates — no new background service is introduced.
/// </para>
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class ReenrichController(
    IAdminMediaReenrichment adminReenrichment,
    IStationScopeProvider scopeProvider,
    ILogger<ReenrichController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/media/{id}/reenrich?fields=&lt;csv&gt;
    ///
    /// Sentinel-resets the enrichment column group(s) named by <paramref name="fields"/> on the
    /// track identified by <paramref name="id"/>. Reaches any existing track regardless of station
    /// scope (SPEC F43.3, closes gitea-#203: scope is a curation filter, not an access gate).
    ///
    /// <para>
    /// <b>fields format:</b> comma-separated tokens (case-insensitive).
    /// Valid tokens: <c>cue</c>, <c>energy</c>, <c>loudness</c>, <c>tags</c>, <c>bpm</c>, <c>year</c>,
    /// <c>all</c>. Missing or empty → <c>all</c> (reset all six groups in one transaction).
    /// Unknown token → 400 (nothing written).
    /// </para>
    ///
    /// <para>
    /// <b>Per-group semantics (SPEC F20.10, F46.4, F48.6):</b>
    ///   <c>cue</c>     — cue_in_sec, cue_out_sec, cue_analyzed_at → NULL; state unchanged.
    ///   <c>energy</c>  — intro_energy, outro_energy, energy_analyzed_at → NULL; state unchanged.
    ///   <c>loudness</c>— integrated_lufs, true_peak_dbtp, measurable → NULL; state = 'discovered'.
    ///   <c>tags</c>    — tags_edited_at → NULL; state = 'discovered'.
    ///   <c>bpm</c>     — bpm, bpm_analyzed_at → NULL; state unchanged.
    ///   <c>year</c>    — year_lookup_at → NULL ONLY (year itself untouched); state unchanged.
    /// </para>
    ///
    /// Response: 202 (body-less) on success; 400 unknown fields; 404 unknown id.
    /// </summary>
    [HttpPost("media/{id:long}/reenrich")]
    public async Task<IActionResult> Reenrich(
        long id,
        [FromQuery] string? fields,
        CancellationToken ct)
    {
        if (!ReenrichFieldsParser.TryParse(fields, out var parsedFields))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid fields value.",
                Detail = $"Unknown field token in '{fields}'. Valid values: cue, energy, loudness, tags, bpm, year, all.",
            });
        }

        // scopeProvider.Current is read fresh on every call (SPEC F30.1) — the P9 stale-snapshot
        // finding this endpoint must not repeat. It is still passed through (interface-shape
        // stability) even though it no longer gates this write (SPEC F43.3).
        var result = await adminReenrichment.ScheduleAsync(
            id.ToString(CultureInfo.InvariantCulture),
            parsedFields,
            scopeProvider.Current,
            ct);

        return result switch
        {
            ReenrichResult.Scheduled => StatusCode(StatusCodes.Status202Accepted),
            ReenrichResult.NotFound  => NotFound(),
            _                        => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// POST /api/media/bulk/reenrich
    ///
    /// Sentinel-resets the enrichment column group(s) named by <c>fields</c> on every track
    /// matching <c>filter</c> within the station's library scope. Returns the number of tracks
    /// scheduled. The existing enricher worker reclaims affected rows at its 50/tick cap.
    ///
    /// <para>
    /// <b>Security:</b>
    ///   • Requires Content-Type: application/json (CSRF guard).
    ///   • Scope-bounded: <c>library_id = ANY(@libraryIds)</c> always present.
    ///   • Empty scope → 0 (default-deny — never a full-table update).
    ///   • All filter values are Npgsql parameters; no value concatenated into SQL.
    /// </para>
    ///
    /// Response: 200 { scheduled: &lt;count&gt; }
    /// </summary>
    [HttpPost("media/bulk/reenrich")]
    [Consumes("application/json")]
    public async Task<IActionResult> BulkReenrich(
        [FromBody] BulkReenrichRequest request,
        CancellationToken ct)
    {
        if (!ReenrichFieldsParser.TryParse(request.Fields, out var parsedFields))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid fields value.",
                Detail = "Unknown field token in fields array. Valid values: cue, energy, loudness, tags, bpm, year, all.",
            });
        }

        var filter = request.Filter;

        // Named library-id overrides the station rotation scope (F23.3 / STORY-065): the named
        // library becomes the effective scope whether or not it falls inside the station rotation.
        // An unnamed filter stays bounded by the station scope.
        var (scope, _) = EffectiveScope.Resolve(scopeProvider.Current, filter?.LibraryId);

        var mediaQuery = new MediaQuery(
            State:       filter?.State,
            Artist:      filter?.Artist,
            Genre:       filter?.Genre,
            LibraryId:   filter?.LibraryId,
            Q:           filter?.Q,
            ArtistExact: filter?.ArtistExact,
            AlbumExact:  filter?.AlbumExact,
            GenresExact: filter?.GenresExact);

        var scheduled = await adminReenrichment.ScheduleBulkAsync(mediaQuery, parsedFields, scope, ct);

        logger.LogInformation(
            "BulkReenrich fields={Fields} filter={@Filter} scheduled={Scheduled}",
            parsedFields, filter, scheduled);

        return Ok(new { scheduled });
    }
}
