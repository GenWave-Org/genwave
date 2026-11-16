namespace GenWave.Memory;

/// <summary>The outcome of drafting lore: the status and, when <see cref="LoreDraftStatus.Drafted"/>, the lines to store.</summary>
public sealed record LoreDraft(LoreDraftStatus Status, IReadOnlyList<string> Lines);
