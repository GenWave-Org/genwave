namespace GenWave.Host.Api;

/// <summary>Body of <c>POST /api/personas/{id}/memory</c>: one authored lore entry. Nullable so a missing field is a
/// plain 400 ProblemDetails, not a model-binding error.</summary>
public sealed record PersonaMemoryLoreRequest(string? Text);
