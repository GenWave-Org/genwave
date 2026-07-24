namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/about</c> (SPEC F62.8, F65.3): the station's public
/// identity panel. Sourced from a mix of live config and build-time constants — never the admin
/// settings DTO — so a future admin-only field never leaks here by accident (F62.9
/// disclosure-by-construction).
/// </summary>
/// <param name="StationName">The operator-configured station name (<c>Station:Name</c>), read live.</param>
/// <param name="Version">
/// The build-stamped <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/> on the
/// Host assembly (SPEC F65.1) — fixed for the process lifetime, never re-read per request.
/// </param>
/// <param name="License">The project's license identifier. Always <c>AGPL-3.0-or-later</c>.</param>
/// <param name="ProjectUrl">The canonical public repository URL.</param>
/// <param name="StreamUrl">
/// The public Icecast stream URL (<c>Station:PublicStreamUrl</c>), read live. Empty string — never
/// null — when the operator has not set one; the spectator page treats that as "no player".
/// </param>
/// <param name="RequestsEnabled">
/// Whether listener song requests are currently open (<c>Station:Requests:Enabled</c>), read live
/// (SPEC F87.11, STORY-229, PLAN T92) — the one new pinned public field the requests epic adds
/// here. The page's wish form renders only when this is true; when false, the form's absence is
/// the same silence as <c>POST /spectator/api/requests</c>' own 404 — no distinguishable "requests
/// are closed" state either way.
/// </param>
public sealed record SpectatorAbout(
    string StationName,
    string Version,
    string License,
    string ProjectUrl,
    string StreamUrl,
    bool RequestsEnabled);
