namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for a persona row (SPEC F35.4): <c>id</c>/<c>name</c>/<c>backstory</c>/<c>style</c>/
/// <c>voice</c> only. Mirrors <see cref="Auth.LibraryDto"/>'s minimal-fields discipline — the spec's
/// documented shape for <c>GET /api/personas</c> and the POST/PATCH response bodies omits
/// <c>created_at</c>/<c>updated_at</c>, so this DTO doesn't echo every <c>Persona</c> column.
/// </summary>
public sealed record PersonaDto(long Id, string Name, string Backstory, string Style, string Voice);
