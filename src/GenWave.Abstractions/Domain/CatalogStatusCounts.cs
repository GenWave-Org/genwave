namespace GenWave.Core.Domain;

/// <summary>
/// The catalog half of the <c>GET /api/status</c> aggregate (SPEC F28.6): row counts by state
/// across the whole catalog, plus how many rows are actually playable within a given scope —
/// <c>ready + measurable + eligible</c>, the exact predicate <see cref="Abstractions.IMediaCatalog.GetRandomReadyAsync"/>
/// (and therefore <c>/internal/safe-track</c>) uses to pick a track. A depleted SafeScope
/// (<see cref="Playable"/> == 0 while its libraries are non-empty) is visible here before the
/// engine ever hits a 204.
/// </summary>
public sealed record CatalogStatusCounts(
    int Ready,
    int Enriching,
    int Failed,
    int Unavailable,
    int Playable);
