namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/shows</c> (create) and <c>PATCH /api/shows/{slug}</c> (edit) (SPEC
/// F115.1, F115.4). <see cref="Name"/> is required, non-blank — <see cref="Core.Abstractions.IShowStore"/>'s
/// own <see cref="Core.Domain.ShowWriteResult.InvalidName"/> rejects a blank/whitespace one, or one
/// whose derived slug equals the reserved fallback literal; <see cref="Tagline"/>/<see cref="Flavor"/>
/// are optional and clear to <c>null</c> when omitted or blank. All fields are nullable here, mirroring
/// <see cref="PersonaRequest"/>'s own all-nullable shape, so the controller produces a typed 400 for a
/// blank/missing name instead of an ASP.NET model-binder 400.
/// </summary>
public sealed record ShowRequest(string? Name, string? Tagline, string? Flavor);
