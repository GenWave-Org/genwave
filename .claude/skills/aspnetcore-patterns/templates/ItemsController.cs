namespace GenWave.Example.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// REST controller template following the house conventions
/// (DEVELOPMENT_CONVENTION.md): plural kebab-case resource routes,
/// verbs by HTTP method, request/response DTOs (never entities),
/// CancellationToken on every action, ProblemDetails for errors,
/// pagination via query string. Adapt: rename "schedule-templates"
/// and the DTOs to your resource. DTO records live in their own files
/// in real code (one type per file).
/// </summary>
[ApiController]
[Authorize]
[Route("api/schedule-templates")]
public sealed class ScheduleTemplatesController(
    IScheduleTemplateService service) : ControllerBase
{
    /// <summary>GET /api/schedule-templates?page=1&amp;limit=20&amp;sort=name-asc</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<ScheduleTemplateResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string sort = "name-asc",
        CancellationToken ct = default)
    {
        var result = await service.GetPageAsync(page, limit, sort, ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<ScheduleTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var template = await service.FindAsync(id, ct);
        return template is null ? NotFound() : Ok(template);
    }

    [HttpPost]
    [ProducesResponseType<ScheduleTemplateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        CreateScheduleTemplateRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>PATCH = partial update; PUT would replace the whole resource.</summary>
    [HttpPatch("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id, UpdateScheduleTemplateRequest request, CancellationToken ct)
    {
        var updated = await service.UpdateAsync(id, request, ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await service.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }
}

// --- DTOs (each in its own file in real code) ---

public sealed record CreateScheduleTemplateRequest(string Name, string Description);

public sealed record UpdateScheduleTemplateRequest(string? Name, string? Description);

public sealed record ScheduleTemplateResponse(int Id, string Name, string Description);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int Limit, int Total);
