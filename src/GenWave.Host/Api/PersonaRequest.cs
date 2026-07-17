namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/personas</c> (create) and <c>PATCH /api/personas/{id}</c> (edit)
/// (SPEC F35.4). <see cref="Name"/> is required, non-blank; <see cref="Backstory"/>,
/// <see cref="Style"/>, and <see cref="Voice"/> are all optional and default to <c>""</c> when
/// omitted (<c>""</c> voice is the station-default sentinel — <see cref="Core.Domain.Persona.Voice"/>).
/// All fields are nullable here so the controller produces a typed 400 for a blank/missing name
/// instead of a model-binder 400.
/// </summary>
public sealed record PersonaRequest(string? Name, string? Backstory, string? Style, string? Voice);
