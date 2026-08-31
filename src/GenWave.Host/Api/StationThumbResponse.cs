namespace GenWave.Host.Api;

/// <summary>
/// Response of <c>POST /api/booth-log/{id}/station-thumb</c> (SPEC F150.1, F150.8; STORY-370, PLAN
/// T367). Unlike the public spectator surface's no-oracle constant-202 (SPEC F150.3 — a
/// public-surface rule only), the operator MAY be told which of <see cref="ThumbWriteResult"/>'s four
/// outcomes this thumb landed as — <see cref="Result"/> is the lowercase token name
/// (<c>"recorded"</c>/<c>"unchanged"</c>/<c>"flipped"</c>/<c>"ignored"</c>), always 200: even
/// <c>"ignored"</c> (a safe-scope row or an unknown media id, SPEC F150.1) is a successful,
/// side-effect-free response, never an error.
/// </summary>
public sealed record StationThumbResponse(string Result);
