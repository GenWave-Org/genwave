namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/play-history</c> (SPEC F62.6): the station's recent play
/// history, newest first, capped at 20 entries. <see cref="Entries"/> is deliberately typed
/// <c>object</c> per element rather than a shared base type — <see cref="SpectatorPlayHistoryTrackEntry"/>
/// and <see cref="SpectatorPlayHistoryPatterEntry"/> are two distinct, unrelated record shapes (no
/// common interface to serialize through), and <c>System.Text.Json</c> only serializes by an
/// element's runtime type when the declared element type is exactly <c>object</c>.
/// </summary>
/// <param name="Entries">Newest-first history entries, at most 20.</param>
public sealed record SpectatorPlayHistoryResponse(IReadOnlyList<object> Entries);
