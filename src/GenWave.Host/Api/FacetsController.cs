using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Facet enumeration endpoint for the Admin UI curation pickers (SPEC F52.1–F52.2, closes gitea-#189).
///
/// Kept separate from <see cref="MediaController"/> — mirrors <see cref="ReenrichController"/>'s
/// stated rationale: this is the only Host consumer of <see cref="IMediaCatalog"/>'s wider surface
/// (<see cref="IMediaCatalog.GetFacetsAsync"/>), so the browse/write test doubles built around
/// <see cref="IAdminMediaQuery"/>/<see cref="IAdminMediaWrite"/> never need to also fake it.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class FacetsController(
    IMediaCatalog catalog,
    IStationScopeProvider scopeProvider) : ControllerBase
{
    /// <summary>
    /// GET /api/media/facets?field=artist|album|genre&amp;library-id=
    ///
    /// Distinct, case-insensitively-grouped values of the named column with row counts
    /// (SPEC F52.1): <c>[{ value, count }]</c>, camelCase, ordered by <c>value</c>
    /// case-insensitively. No pagination — bounded by catalog cardinality at single-operator scale.
    ///
    /// Scoping mirrors <see cref="MediaController.List"/> (SPEC F52.2): bounded by the station's
    /// rotation scope by default; <c>?library-id=</c> names the effective scope instead (F23.3,
    /// via the shared <see cref="EffectiveScope"/> helper), whether or not that library falls
    /// inside the station's rotation.
    ///
    /// <c>field</c> missing or not one of <c>artist</c>/<c>album</c>/<c>genre</c> → 400,
    /// nothing queried.
    /// </summary>
    [HttpGet("media/facets")]
    public async Task<IActionResult> GetFacets(
        [FromQuery] string? field,
        [FromQuery(Name = "library-id")] long? libraryId,
        CancellationToken ct)
    {
        if (!TryParseField(field, out var parsedField))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid field.",
                Detail = "field must be one of: artist, album, genre.",
            });
        }

        // Named library-id overrides the station rotation scope (F23.3), exactly like browse —
        // scopeProvider.Current is read fresh on every call (SPEC F30.1).
        var (scope, _) = EffectiveScope.Resolve(scopeProvider.Current, libraryId);

        var facets = await catalog.GetFacetsAsync(parsedField, scope, ct);

        return Ok(facets);
    }

    /// <summary>Case-insensitive match against the three valid facet field values (F52.1).</summary>
    static bool TryParseField(string? field, out FacetField parsed)
    {
        switch (field?.Trim().ToLowerInvariant())
        {
            case "artist":
                parsed = FacetField.Artist;
                return true;
            case "album":
                parsed = FacetField.Album;
                return true;
            case "genre":
                parsed = FacetField.Genre;
                return true;
            default:
                parsed = default;
                return false;
        }
    }
}
