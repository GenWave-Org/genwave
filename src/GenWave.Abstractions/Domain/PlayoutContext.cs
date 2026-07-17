namespace GenWave.Core.Domain;

/// <summary>
/// What the feeder tells the selection seam about the current moment (PRD §4.1). Small by design and
/// grows without breaking the <see cref="Abstractions.INextItemProvider"/> signature. v1 carries the
/// recently-aired media ids so a "random" strategy can avoid repeats.
/// </summary>
public sealed record PlayoutContext(IReadOnlyList<string> RecentMediaIds);
