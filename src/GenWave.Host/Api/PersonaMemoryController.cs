using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;

namespace GenWave.Host.Api;

/// <summary>
/// The operator's view of one DJ's memory (SPEC F218.19, F218.20, F218.24; STORY-538, STORY-540; PLAN T705, T706):
/// <c>GET/POST /api/personas/{id}/memory</c>, <c>POST /api/personas/{id}/memory/suggest</c>,
/// <c>PATCH/DELETE /api/personas/{id}/memory/{rowId}</c>.
/// A sibling of <see cref="PersonaAvatarController"/>, with the same <see cref="AuthorizationPolicies.Settings"/>
/// pairing: memory is part of the persona's own editable identity. It carries <see cref="AdminSurfaceAttribute"/>,
/// never a spectator marker, so no public surface reaches it (F218.26). Every action resolves the persona first
/// (an unknown persona is 404) and every store call is scoped by persona AND row id, so a row of another persona
/// is a 404 too. Free text is trimmed and bounded; it is never written to a log.
/// </summary>
[ApiController]
[Route("api/personas/{id:long}/memory")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class PersonaMemoryController(
    IPersonaStore personaStore,
    IPersonaMemoryAdmin memory,
    LoreDrafter loreDrafter,
    ILogger<PersonaMemoryController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long id, [FromQuery] string? kind, CancellationToken ct)
    {
        if (kind is not null && !MemoryKinds.OperatorShaped.Contains(kind, StringComparer.Ordinal))
            return BadRequest(Problem400("kind must be lore, episode or life."));
        if (await personaStore.GetByIdAsync(id, ct) is null)
            return NotFound(UnknownPersonaProblem(id));

        var rows = await memory.ListForAdminAsync(id, kind, ct);
        return Ok(rows.Select(PersonaMemoryRowDto.From).ToList());
    }

    [HttpPost]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> AddLore(long id, [FromBody] PersonaMemoryLoreRequest request, CancellationToken ct)
    {
        if (await personaStore.GetByIdAsync(id, ct) is null)
            return NotFound(UnknownPersonaProblem(id));
        if (TrimmedTextProblem(request.Text) is { } problem)
            return BadRequest(problem);
        var text = request.Text?.Trim() ?? string.Empty;

        var row = await memory.AddAuthoredLoreAsync(id, text, ct);
        logger.LogInformation("Persona memory lore added personaId={PersonaId} rowId={RowId}", id, row.Id);
        // There is no single-row GET, so the location is the list the new row appears in.
        return Created($"/api/personas/{id}/memory", PersonaMemoryRowDto.From(row));
    }

    /// <summary>
    /// <c>POST /api/personas/{id}/memory/suggest</c> (SPEC F218.20): drafts up to five lore rows from the persona's soul
    /// and backstory, stored <c>source=drafted</c> and pending until approved. A model that is not set up is 409, a model
    /// fault or an unusable reply is 502; nothing is stored in either case, and no prompt or reply text is logged.
    /// </summary>
    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest(long id, CancellationToken ct)
    {
        if (await personaStore.GetByIdAsync(id, ct) is not { } persona)
            return NotFound(UnknownPersonaProblem(id));

        var soul = (await personaStore.GetCardByIdAsync(id, ct))?.Soul ?? string.Empty;
        if (string.IsNullOrWhiteSpace(soul) && string.IsNullOrWhiteSpace(persona.Backstory))
            return BadRequest(Problem400("Give this DJ a soul or a backstory first."));

        var existing = (await memory.ListForAdminAsync(id, MemoryKinds.Lore, ct)).Select(r => r.Content).ToList();
        LoreDraft draft;
        try
        {
            draft = await loreDrafter.DraftAsync(soul, persona.Backstory, existing, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Lore suggestion failed personaId={PersonaId} error={ErrorType}", id, ex.GetType().Name);
            return Problem502("The language model did not answer.");
        }

        switch (draft.Status)
        {
            case LoreDraftStatus.NotConfigured:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Language model not set up.",
                    Detail = "No language model is set up.",
                });
            case LoreDraftStatus.NoUsableLines:
                return Problem502("The language model gave no usable lore.");
        }

        var rows = await memory.AddDraftedLoreAsync(id, draft.Lines, ct);
        logger.LogInformation("Persona lore drafted personaId={PersonaId} count={Count}", id, rows.Count);
        return Created($"/api/personas/{id}/memory", rows.Select(PersonaMemoryRowDto.From).ToList());
    }

    [HttpPatch("{rowId:long}")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Patch(long id, long rowId, [FromBody] PersonaMemoryPatchRequest request, CancellationToken ct)
    {
        if (request.Text is null && request.Approved is null && request.Forgotten is null)
            return BadRequest(Problem400("Send text, approved or forgotten."));
        if (request.Approved == false)
            return BadRequest(Problem400("approved can only be true."));
        if (request.Text is not null && TrimmedTextProblem(request.Text) is { } problem)
            return BadRequest(problem);
        if (await personaStore.GetByIdAsync(id, ct) is null)
            return NotFound(UnknownPersonaProblem(id));

        var change = new PersonaMemoryChange(request.Text?.Trim(), request.Approved == true, request.Forgotten);
        var result = await memory.UpdateAsync(id, rowId, change, ct);
        switch (result.Status)
        {
            case PersonaMemoryUpdateStatus.NotFound:
                return NotFound(UnknownRowProblem(id, rowId));
            case PersonaMemoryUpdateStatus.NotDrafted:
                return BadRequest(Problem400("Only a drafted row can be approved."));
        }

        logger.LogInformation("Persona memory row changed personaId={PersonaId} rowId={RowId}", id, rowId);
        return result.Row is { } row ? Ok(PersonaMemoryRowDto.From(row)) : NotFound(UnknownRowProblem(id, rowId));
    }

    [HttpDelete("{rowId:long}")]
    public async Task<IActionResult> Delete(long id, long rowId, CancellationToken ct)
    {
        if (await personaStore.GetByIdAsync(id, ct) is null)
            return NotFound(UnknownPersonaProblem(id));
        if (!await memory.DeleteAsync(id, rowId, ct))
            return NotFound(UnknownRowProblem(id, rowId));

        logger.LogInformation("Persona memory row removed personaId={PersonaId} rowId={RowId}", id, rowId);
        return NoContent();
    }

    static ProblemDetails? TrimmedTextProblem(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Problem400("text must not be blank.");
        return trimmed.Length > MemoryText.MaxChars ? Problem400($"text must be at most {MemoryText.MaxChars} characters.") : null;
    }

    static ProblemDetails Problem400(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Validation error.",
        Detail = detail,
    };

    ObjectResult Problem502(string detail) => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
    {
        Status = StatusCodes.Status502BadGateway,
        Title = "Language model unavailable.",
        Detail = detail,
    });

    static ProblemDetails UnknownPersonaProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title = "Not found.",
        Detail = $"No persona with id {id} exists.",
    };

    static ProblemDetails UnknownRowProblem(long id, long rowId) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title = "Not found.",
        Detail = $"Persona {id} has no memory row {rowId}.",
    };
}
