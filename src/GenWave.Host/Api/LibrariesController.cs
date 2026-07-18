using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Auth;

namespace GenWave.Host.Api;

/// <summary>
/// Admin library management endpoints for the single-station deployment (STORY-047, Epic J).
///
/// Replaces the scope-filtered <c>GET /api/libraries</c> that previously lived in
/// <see cref="AuthController"/> (STORY-026). The new contract:
/// <list type="bullet">
///   <item>GET returns EVERY library row (not scope-filtered) with a per-row media count.</item>
///   <item>POST creates a new library; does NOT auto-add it to Station:Scope:LibraryIds.</item>
///   <item>PATCH renames an existing library (including the seed id=1).</item>
///   <item>DELETE removes an empty library; rejects non-empty with 409 + dependentMediaCount.</item>
/// </list>
///
/// Security: deny-by-default (cookie auth when Admin:Password is set). POST and PATCH require
/// Content-Type: application/json (<c>[Consumes]</c> attribute — CSRF guard mirrors W2).
/// </summary>
[ApiController]
[Route("api/libraries")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class LibrariesController(
    ILibraryRepository libraryRepository,
    IAdminLibraryWrite adminWrite,
    ILogger<LibrariesController> logger) : ControllerBase
{
    /// <summary>
    /// GET /api/libraries — every library row with id, name, and media count.
    /// NOT filtered by Station:Scope:LibraryIds — library management operates above scope (AC1).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var libraries = await libraryRepository.GetAllWithMediaCountAsync(ct);
        return Ok(libraries
            .Select(l => new LibraryDto(l.Id, l.Name, l.MediaCount))
            .ToArray());
    }

    /// <summary>
    /// POST /api/libraries — create a library with the given name.
    /// 201 with { id, name, mediaCount: 0 } on success; 400 for blank name; 409 for duplicate name.
    /// The new library is NOT added to Station:Scope:LibraryIds (AC7).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create(
        [FromBody] LibraryNameRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Validation error.",
                Detail = "name must not be blank or whitespace.",
            });
        }

        var name   = request.Name.Trim();
        var result = await adminWrite.CreateAsync(name, ct);

        if (result is LibraryWriteResult.Created created)
            logger.LogInformation("Library created id={LibraryId} name={LibraryName}", created.Id, name);

        return result switch
        {
            LibraryWriteResult.Created c =>
                StatusCode(StatusCodes.Status201Created, new LibraryDto(c.Id, name, 0)),

            LibraryWriteResult.NameConflict =>
                Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title  = "Name conflict.",
                    Detail = $"A library named \"{name}\" already exists.",
                }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// PATCH /api/libraries/{id} — rename an existing library.
    /// 200 on success; 400 for blank name; 404 for unknown id; 409 for duplicate name.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Rename(
        long id,
        [FromBody] LibraryNameRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Validation error.",
                Detail = "name must not be blank or whitespace.",
            });
        }

        var name   = request.Name.Trim();
        var result = await adminWrite.RenameAsync(id, name, ct);

        if (result is LibraryWriteResult.Renamed)
            logger.LogInformation("Library renamed id={LibraryId} newName={LibraryName}", id, name);

        return result switch
        {
            LibraryWriteResult.Renamed => Ok(),

            LibraryWriteResult.NotFound =>
                NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title  = "Not found.",
                    Detail = $"No library with id {id} exists.",
                }),

            LibraryWriteResult.NameConflict =>
                Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title  = "Name conflict.",
                    Detail = $"A library named \"{name}\" already exists.",
                }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// DELETE /api/libraries/{id} — remove an empty library.
    /// 204 on success; 404 for unknown id; 409 with body { dependentMediaCount } if media rows still reference it.
    /// </summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var result = await adminWrite.DeleteAsync(id, ct);

        if (result is LibraryWriteResult.Deleted)
            logger.LogInformation("Library deleted id={LibraryId}", id);

        return result switch
        {
            LibraryWriteResult.Deleted => NoContent(),

            LibraryWriteResult.NotFound =>
                NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title  = "Not found.",
                    Detail = $"No library with id {id} exists.",
                }),

            LibraryWriteResult.HasDependents h =>
                Conflict(BuildHasDependentsProblem(id, h.DependentMediaCount)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static ProblemDetails BuildHasDependentsProblem(long id, int dependentMediaCount)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title  = "Library has dependents.",
            Detail = $"Library {id} still has {dependentMediaCount} media row(s). Reassign them before deleting.",
        };
        problem.Extensions["dependentMediaCount"] = dependentMediaCount;
        return problem;
    }
}
