namespace GenWave.Core.Domain;

/// <summary>One accrued episode for a persona (SPEC F218.15): what it remembers and when it happened.</summary>
/// <param name="PersonaId">The persona who remembers it.</param>
/// <param name="Content">The memory text.</param>
/// <param name="OccurredAt">When it happened (a show's start, the moment a fact was read).</param>
public sealed record NewEpisode(long PersonaId, string Content, DateTimeOffset OccurredAt);
