namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/personas/preview</c> (SPEC F35.6). The persona to preview is one
/// of three mutually-exclusive shapes, checked in this order by <see cref="PersonaController"/>:
/// <list type="number">
///   <item><see cref="PersonaId"/> — preview a saved persona by id (400 if unknown).</item>
///   <item>Any of <see cref="Name"/>/<see cref="Backstory"/>/<see cref="Style"/>/<see cref="Voice"/>
///   present — an unsaved draft persona, never persisted.</item>
///   <item>Neither — the currently active persona (may itself be none).</item>
/// </list>
/// <see cref="Kind"/> defaults to <c>LeadIn</c> when omitted; <see cref="MediaId"/> is optional —
/// absent yields a null-<c>Track</c> segment request (both the template renderer and the LLM
/// prompt builder already handle that).
/// </summary>
public sealed record PersonaPreviewRequest(
    string? Kind,
    long?   MediaId,
    long?   PersonaId,
    string? Name,
    string? Backstory,
    string? Style,
    string? Voice);
