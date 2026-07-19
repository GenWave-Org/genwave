using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// DJ persona CRUD for the Admin UI (SPEC F35.4, STORY-120): <c>GET/POST/PATCH/DELETE /api/personas</c>
/// over <see cref="IPersonaStore"/>.
///
/// DELIBERATE F18.6 DEVIATION — NO <c>If-Match</c>/ETag anywhere in this controller. Every other
/// admin write surface (media tags, library rename) carries optimistic concurrency because a
/// background enricher can race an operator edit; personas have a single writer (the operator, via
/// this controller) and no background contender — there is nothing for an ETag to protect against.
/// A reviewer seeing If-Match reintroduced here should ask why: it would be ceremony with no
/// concurrency bug behind it (SPEC F35.4, ARCHITECTURE "Personas (F35)").
///
/// Security: deny-by-default cookie auth (same fallback policy as every other <c>/api/*</c>
/// controller). Writes require <c>Content-Type: application/json</c> (<see cref="ConsumesAttribute"/>
/// — 415 otherwise, F18.7).
/// </summary>
[ApiController]
[Route("api/personas")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class PersonaController(
    IPersonaStore personaStore,
    IStationSettingsStore settingsStore,
    IOptionsMonitor<StationOptions> stationMonitor,
    IPersonaPreviewWriter previewWriter,
    IActivePersonaAccessor personaAccessor,
    IAdminMediaLookup mediaLookup,
    IStationScopeProvider scopeProvider,
    ILogger<PersonaController> logger) : ControllerBase
{
    // The F19 allowlist key this controller's delete-clears-active write targets (F35.5).
    internal const string ActiveIdKey = "Station:Persona:ActiveId";

    /// <summary>GET /api/personas — every persona row, ordered by name (F35.4).</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var personas = await personaStore.GetAllAsync(ct);
        return Ok(personas.Select(ToDto).ToArray());
    }

    /// <summary>
    /// POST /api/personas — create a persona. 201 with the row on success; 400 for a blank/missing
    /// name; 409 for a duplicate name (F35.4).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] PersonaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(BlankNameProblem());

        var draft = ToDraft(request);
        var result = await personaStore.CreateAsync(draft, ct);

        if (result is PersonaWriteResult.Created created)
            logger.LogInformation(
                "Persona created id={PersonaId} name={PersonaName}", created.Persona.Id, created.Persona.Name);

        return result switch
        {
            PersonaWriteResult.Created c => StatusCode(StatusCodes.Status201Created, ToDto(c.Persona)),
            PersonaWriteResult.NameConflict => Conflict(NameConflictProblem(draft.Name)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// PATCH /api/personas/{id} — edit an existing persona. 200 with the row on success; 400 for a
    /// blank/missing name; 404 for an unknown id; 409 for a duplicate name (F35.4). No
    /// <c>If-Match</c> — see the class header.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Update(long id, [FromBody] PersonaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(BlankNameProblem());

        var draft = ToDraft(request);
        var result = await personaStore.UpdateAsync(id, draft, ct);

        if (result is PersonaWriteResult.Updated updated)
            logger.LogInformation(
                "Persona updated id={PersonaId} name={PersonaName}", updated.Persona.Id, updated.Persona.Name);

        return result switch
        {
            PersonaWriteResult.Updated u => Ok(ToDto(u.Persona)),
            PersonaWriteResult.NotFound => NotFound(NotFoundProblem(id)),
            PersonaWriteResult.NameConflict => Conflict(NameConflictProblem(draft.Name)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// DELETE /api/personas/{id} — remove a persona. 204 on success; 404 for an unknown id.
    /// Deleting the currently active persona clears <c>Station:Persona:ActiveId</c> back to
    /// <c>0</c> IN THE SAME REQUEST (F35.5) — a stale id reached any other way still degrades
    /// safely via <see cref="IActivePersonaAccessor"/>, but this is the one path that can prevent
    /// the staleness outright.
    /// </summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var result = await personaStore.DeleteAsync(id, ct);

        if (result is PersonaWriteResult.Deleted)
        {
            logger.LogInformation("Persona deleted id={PersonaId}", id);

            if (stationMonitor.CurrentValue.Persona.ActiveId == id)
            {
                // WriteAsync raises the overlay reload token — the very next
                // IOptionsMonitor<StationOptions> read (including this request's own, were it to
                // read again) sees ActiveId=0.
                await settingsStore.WriteAsync(ActiveIdKey, 0, ct);
                logger.LogInformation(
                    "Cleared {Key} after deleting the active persona id={PersonaId}", ActiveIdKey, id);
            }
        }

        return result switch
        {
            PersonaWriteResult.Deleted => NoContent(),
            PersonaWriteResult.NotFound => NotFound(NotFoundProblem(id)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// POST /api/personas/preview — auditions a persona's copy through the REAL
    /// <see cref="IPersonaPreviewWriter"/> (SPEC F35.6, STORY-123): 200 <c>{ text }</c> on success;
    /// 502 ProblemDetails on any LLM failure — NEVER a silently-substituted template (that would
    /// misrepresent the persona being auditioned). The persona previewed is, in order: a saved
    /// persona by <c>personaId</c> (400 if unknown — it is a body field, not a route id, so this
    /// mirrors the 400 the other body-field validations in this controller already use rather than
    /// 404); an unsaved draft built from whichever of <c>name</c>/<c>backstory</c>/<c>style</c>/
    /// <c>voice</c> are present; or, when neither is given, whatever persona is active right now
    /// (resolved through the exact same <see cref="IActivePersonaAccessor"/> seam the on-air writer
    /// itself uses — the natural default).
    ///
    /// <c>mediaId</c> is optional; when present it is resolved through the SAME
    /// <see cref="IAdminMediaLookup"/> + <see cref="IStationScopeProvider"/> pair
    /// <c>MediaController.GetById</c> uses (400 unknown id, 403 out-of-scope) — this is a read of
    /// track metadata for prompt building, not a new authorization surface, so it reuses the
    /// existing IDOR-safe posture rather than inventing a scope-free "ratings-style" read. Absent
    /// <c>mediaId</c> yields a null-<c>Track</c> segment request; both the template renderer and
    /// <c>LlmCopyWriter</c>'s prompt builder already handle that.
    /// </summary>
    [HttpPost("preview")]
    [Consumes("application/json")]
    public async Task<IActionResult> Preview([FromBody] PersonaPreviewRequest request, CancellationToken ct)
    {
        if (!TryParseKind(request.Kind, out var kind))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid kind.",
                Detail = $"kind must be one of: {string.Join(", ", Enum.GetNames<SegmentKind>())}.",
            });
        }

        var (persona, personaError) = await ResolvePreviewPersonaAsync(request, ct);
        if (personaError is not null)
            return personaError;

        var (track, trackError) = await ResolveTrackAsync(request.MediaId, ct);
        if (trackError is not null)
            return trackError;

        var station = stationMonitor.CurrentValue;
        var segmentRequest = new SegmentRequest(
            kind, ResolvePreviewVoice(persona, station.Voice), station.Name, track, DateTimeOffset.UtcNow, station.Id);

        var result = await previewWriter.WritePreviewAsync(segmentRequest, persona, ct);

        return result switch
        {
            PersonaPreviewResult.Success s => Ok(new PersonaPreviewResponse(s.Text)),
            PersonaPreviewResult.Failed f => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title  = "Persona preview failed.",
                Detail = f.Detail,
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the persona <see cref="Preview"/> should use, per the three-way precedence
    /// documented on <see cref="Preview"/>'s remarks. Returns a non-null <see cref="IActionResult"/>
    /// error only for an unknown <c>personaId</c>; every other path always succeeds (a draft persona
    /// can always be built, and the active-persona accessor never throws).
    /// </summary>
    async Task<(Persona? Persona, IActionResult? Error)> ResolvePreviewPersonaAsync(
        PersonaPreviewRequest request, CancellationToken ct)
    {
        if (request.PersonaId is { } personaId)
        {
            var found = await personaStore.GetByIdAsync(personaId, ct);
            if (found is null)
                return (null, BadRequest(UnknownPersonaProblem(personaId)));

            return (found, null);
        }

        if (HasDraftFields(request))
        {
            var now = DateTime.UtcNow;
            var name = string.IsNullOrWhiteSpace(request.Name) ? "Draft persona" : request.Name.Trim();
            return (new Persona(
                0, name, request.Backstory ?? string.Empty, request.Style ?? string.Empty,
                request.Voice ?? string.Empty, now, now), null);
        }

        // Neither personaId nor draft fields — preview whatever persona is live on-air right now,
        // through the exact seam the LLM writer itself resolves it through (F35.6's natural default).
        return (await personaAccessor.ResolveAsync(ct), null);
    }

    static bool HasDraftFields(PersonaPreviewRequest request) =>
        !string.IsNullOrEmpty(request.Name) || !string.IsNullOrEmpty(request.Backstory) ||
        !string.IsNullOrEmpty(request.Style) || !string.IsNullOrEmpty(request.Voice);

    /// <summary>
    /// Resolves an optional <c>mediaId</c> to a preview-only <see cref="MediaItem"/> via the exact
    /// lookup + scope check <c>MediaController.GetById</c> uses (400 unknown, 403 out-of-scope).
    /// Loudness/measurement fields on the returned item are placeholders — this track is never
    /// synthesized or measured; only title/artist/album/genre/year reach the LLM prompt
    /// (<c>LlmCopyWriter.BuildUserContent</c>).
    /// </summary>
    async Task<(MediaItem? Track, IActionResult? Error)> ResolveTrackAsync(long? mediaId, CancellationToken ct)
    {
        if (mediaId is null)
            return (null, null);

        var found = await mediaLookup.GetByIdWithLibraryAsync(mediaId.Value, ct);
        if (found is null)
            return (null, BadRequest(UnknownMediaProblem(mediaId.Value)));

        var (row, libraryId) = found.Value;
        if (!scopeProvider.Current.LibraryIds.Contains(libraryId))
        {
            // Mirrors MediaController.GetById: log the denial without row data (security-api hard
            // rule gitea-#10) and answer 403, not 404 — house default for an existing-but-out-of-scope row.
            logger.LogWarning(
                "Persona preview media access denied: mediaId={MediaId} reason=out_of_scope", mediaId.Value);
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Access denied." }));
        }

        return (ToPreviewTrack(row), null);
    }

    static MediaItem ToPreviewTrack(AdminMediaDto row) =>
        new(
            row.MediaId,
            row.Locator,
            row.Title ?? row.MediaId,
            // Fully qualified: "Loudness" unqualified resolves to the sibling GenWave.Loudness
            // PROJECT namespace here (both nest under the shared GenWave ancestor), not the
            // Core.Domain struct — the one spot in this file that constructs one directly.
            new Core.Domain.Loudness(row.IntegratedLufs ?? 0, row.TruePeakDbtp ?? 0, row.Measurable ?? false),
            Artist: row.Artist,
            Album: row.Album,
            Genre: row.Genre,
            Year: row.Year);

    static string ResolvePreviewVoice(Persona? persona, string stationVoice) =>
        persona is not null && !string.IsNullOrEmpty(persona.Voice) ? persona.Voice : stationVoice;

    static bool TryParseKind(string? raw, out SegmentKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            kind = SegmentKind.LeadIn;
            return true;
        }

        return Enum.TryParse(raw, ignoreCase: true, out kind);
    }

    static ProblemDetails UnknownPersonaProblem(long id) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown persona.",
        Detail = $"No persona with id {id} exists.",
    };

    static ProblemDetails UnknownMediaProblem(long id) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown media.",
        Detail = $"No media row with id {id} exists.",
    };

    static PersonaDto ToDto(Persona persona) =>
        new(persona.Id, persona.Name, persona.Backstory, persona.Style, persona.Voice);

    // Trims the name and defaults the optional fields to "" — mirrors Persona.Voice's "" = station
    // default sentinel for all three optional fields, not just voice.
    static PersonaDraft ToDraft(PersonaRequest request) =>
        new(
            request.Name?.Trim() ?? string.Empty,
            request.Backstory ?? string.Empty,
            request.Style ?? string.Empty,
            request.Voice ?? string.Empty);

    static ProblemDetails BlankNameProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "name must not be blank or whitespace.",
    };

    static ProblemDetails NameConflictProblem(string name) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Name conflict.",
        Detail = $"A persona named \"{name}\" already exists.",
    };

    static ProblemDetails NotFoundProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No persona with id {id} exists.",
    };
}
