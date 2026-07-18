namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/stats</c> (SPEC F62.7): exactly the three catalog counts
/// that describe library health without disclosing scope configuration. Deliberately excludes
/// <see cref="Core.Domain.CatalogStatusCounts.Unavailable"/> and
/// <see cref="Core.Domain.CatalogStatusCounts.Playable"/> — both would reveal SafeScope sizing to
/// the public (F62.9 disclosure-by-construction: this type simply has no properties for them).
/// </summary>
/// <param name="Ready">Rows enriched, measurable, eligible and playable.</param>
/// <param name="Enriching">Rows discovered but not yet enriched.</param>
/// <param name="Failed">Rows that failed enrichment.</param>
public sealed record SpectatorStats(int Ready, int Enriching, int Failed);
