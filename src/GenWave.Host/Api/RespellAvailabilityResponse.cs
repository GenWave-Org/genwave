namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>GET /api/pronunciations/derive/available</c> (gh-#487): whether the respell
/// assist can currently succeed, off the exact same latched
/// <see cref="Pronunciations.IRespellOracle.IsAvailable"/> the sibling
/// <c>POST /api/pronunciations/derive</c> endpoint pre-checks before ever calling
/// <see cref="Pronunciations.IRespellOracle.DeriveAsync"/>. A cheap pre-flight probe — no process
/// spawn — so the pronunciation rules editor can hide the "Derive" assist BEFORE the operator's
/// first click on an espeak-less image, rather than learning it only after one dead-end 501.
/// </summary>
public sealed record RespellAvailabilityResponse(bool Available);
