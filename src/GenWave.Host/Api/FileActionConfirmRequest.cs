namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/gardener/file-actions/confirm</c>'s own request body (SPEC F154.5; STORY-379; PLAN
/// T381, gh-#529) — the opaque token <c>dry-run</c>'s own 200 response carried, presented back
/// unread by the confirm action (<see cref="GenWave.Core.Abstractions.IFileActionPlanTokens.TryRead"/>
/// is what actually reads it). Declared <see langword="string"/>? so a null/missing token is a plain
/// 400, never a model-binding failure.
/// </summary>
public sealed record FileActionConfirmRequest(string? PlanToken);
