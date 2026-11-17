namespace GenWave.Core.Domain;

/// <summary>One memory row chosen for a callback ask (SPEC F218.40): enough to render it and to settle it.</summary>
/// <param name="Id">The <c>persona_memory.id</c>, for the callback store's settle.</param>
/// <param name="Kind"><c>lore</c>, <c>episode</c> or <c>life</c>.</param>
/// <param name="Content">The claim text.</param>
/// <param name="OccurredAt">When an episode happened (UTC); null for timeless rows.</param>
public sealed record RecalledMemory(long Id, string Kind, string Content, DateTime? OccurredAt);
