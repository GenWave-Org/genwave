namespace GenWave.Core.Domain;

/// <summary>One memory row chosen for a prompt (SPEC F218.22): enough to render it and to mark it aired.</summary>
/// <param name="Id">The <c>persona_memory.id</c>, for <see cref="Abstractions.IMemoryRecallStore.MarkAiredAsync"/>.</param>
/// <param name="Kind"><c>lore</c>, <c>episode</c> or <c>life</c>.</param>
/// <param name="Content">The claim text.</param>
/// <param name="OccurredAt">When an episode happened (UTC); null for timeless rows.</param>
public sealed record RecalledMemory(long Id, string Kind, string Content, DateTime? OccurredAt);
