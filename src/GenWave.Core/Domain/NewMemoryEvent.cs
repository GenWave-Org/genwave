namespace GenWave.Core.Domain;

/// <summary>One shared happening several personas' episodes point at (SPEC F218.15, <c>station.memory_event</c>).</summary>
/// <param name="Kind">The happening's kind, e.g. <c>crosstalk</c>.</param>
/// <param name="Summary">One neutral line describing it.</param>
/// <param name="OccurredAt">When it happened.</param>
public sealed record NewMemoryEvent(string Kind, string Summary, DateTimeOffset OccurredAt);
