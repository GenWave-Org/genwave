using GenWave.Core.Abstractions;

namespace GenWave.Host.Api;

/// <summary>
/// The v1 query API (PRD §7) — the eventual remote form of <see cref="IMediaCatalog"/>. Kept to exactly
/// what selection needs: a by-id lookup and a random-ready pick. Criteria filters, paging, and playlist
/// generation are deliberately out of scope (PRD §13); the schema is indexed so they are additive later.
/// </summary>
static class MediaEndpoints
{
    public static RouteGroupBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        // Group under /media and allow anonymous access — these endpoints are used by the
        // Liquidsoap/Orchestrator internal hot path and MUST NOT require a session cookie.
        var group = app.MapGroup("/media").AllowAnonymous();

        // GET /media/{id} -> MediaReference (404 if absent). A literal "random" segment takes routing
        // precedence over this parameter, so /media/random resolves to the endpoint below.
        // IStationScopeProvider is injected per-request and read at call time (SPEC F30.1) — no
        // scope is captured in a closure here, so a live scope edit applies without an api restart.
        group.MapGet("/{id}", async (string id, IMediaCatalog catalog, IStationScopeProvider scopeProvider, CancellationToken ct) =>
            await catalog.GetByIdAsync(scopeProvider.Current, id, ct) is { } reference
                ? Results.Ok(reference)
                : Results.NotFound());

        // GET /media/random?exclude=id1,id2 -> one ready MediaReference (404 if none ready).
        group.MapGet("/random", async (string? exclude, IMediaCatalog catalog, IStationScopeProvider scopeProvider, CancellationToken ct) =>
        {
            var excludeIds = string.IsNullOrWhiteSpace(exclude)
                ? Array.Empty<string>()
                : exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return await catalog.GetRandomReadyAsync(scopeProvider.Current, excludeIds, ct) is { } reference
                ? Results.Ok(reference)
                : Results.NotFound();
        });

        return group;
    }
}
