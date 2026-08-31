namespace GenWave.Host.Api;

/// <summary>
/// Body of <c>POST /api/booth-log/{id}/station-thumb</c> (SPEC F150.8, STORY-370, PLAN T367).
/// <see cref="Direction"/> is case-insensitive <c>"up"</c> or <c>"down"</c> — mirrors
/// <see cref="TasteThumbRequest.Direction"/>'s own shape (the two are structurally identical by
/// coincidence, never by a shared type: <see cref="TasteThumbRequest"/>'s own remarks give the exact
/// reason — F150.1/F155.3 keep the station thumb, the persona-taste thumb, and the F33 rating ledger
/// disjoint, and a shared request type would blur that boundary in code even though nothing about a
/// request DTO could itself violate it).
/// </summary>
public sealed record StationThumbRequest(string Direction);
