namespace GenWave.Host.Api;

/// <summary>
/// The whole body of <c>GET /api/about</c> (SPEC F207.1/F207.2, STORY-474, PLAN T561) — see
/// <see cref="AboutController.Get"/>'s own remarks for how each field is sourced.
/// </summary>
/// <param name="Version">The build-stamped <c>AssemblyInformationalVersion</c>.</param>
/// <param name="StationName"><c>Station:Name</c>, read live.</param>
/// <param name="Tagline"><c>Station:Tagline</c>, read live; <c>""</c> when unset.</param>
/// <param name="LibraryCount">
/// Music tracks ready to air: rows in <c>library.media</c> with <c>state = 'ready' and
/// imaging_kind is null</c>, unscoped across the whole catalog. Excludes every authored imaging row
/// — ads, jingles, liners, station IDs, promos.
/// </param>
/// <param name="UptimeSeconds">Whole seconds since process start.</param>
/// <param name="Attributions">The SAME projection <c>GET /api/attributions</c> serves.</param>
public sealed record AboutResponse(
    string Version,
    string StationName,
    string Tagline,
    int LibraryCount,
    long UptimeSeconds,
    AttributionsResponse Attributions);
