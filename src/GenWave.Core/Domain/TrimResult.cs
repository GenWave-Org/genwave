namespace GenWave.Core.Domain;

/// <summary>
/// The outcome of cutting repeated sentences out of DJ copy (SPEC F218.35).
/// </summary>
/// <param name="Text">The kept sentences in order, joined by a single space; empty when every sentence was dropped.</param>
/// <param name="DroppedCount">How many sentences were dropped.</param>
public sealed record TrimResult(string Text, int DroppedCount);
