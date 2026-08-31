namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /spectator/api/thumbs</c> (SPEC F150.2, STORY-369, PLAN T366). Exactly
/// two fields — nothing else is bindable, so there is no mass-assignment surface here (no listener
/// identity, no media id: those are derived server-side from the <c>genwave-listener</c> cookie and
/// the <paramref name="Airing"/> token respectively, never client-supplied).
/// </summary>
/// <param name="Airing">
/// The opaque per-airing token <c>GET /spectator/api/now-playing</c> published as <c>airing</c> (SPEC
/// F149.4). Missing, over-length, or outside the base64url charset ⇒ 400 naming the field (a malformed
/// BODY, F87.3's shape) — a WELL-FORMED token that simply fails to resolve to the current or previous
/// airing (SPEC F150.4) is a DIFFERENT case entirely: a silent 202, never 400 (SPEC F150.3's no-oracle
/// posture — see <see cref="SpectatorThumbsController.PostThumb"/>'s own remarks for the distinction).
/// </param>
/// <param name="Direction">
/// <c>"up"</c> or <c>"down"</c>, exactly (case-sensitive) — anything else ⇒ 400 naming the field. Never
/// echoed back on rejection.
/// </param>
public sealed record SpectatorThumbSubmission(string? Airing, string? Direction);
