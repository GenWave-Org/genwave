namespace GenWave.Core.Domain;

/// <summary>
/// What the feeder tells the selection seam about the current moment (PRD §4.1). Small by design and
/// grows without breaking the <see cref="Abstractions.INextItemProvider"/> signature. v1 carries the
/// recently-aired media ids so a "random" strategy can avoid repeats.
/// </summary>
/// <param name="RecentMediaIds">The feeder's anti-repeat ring, oldest-first, most-recent LAST.</param>
/// <param name="QueuedAheadMs">
/// gh-#254 — best-effort milliseconds of runtime already committed AHEAD of anything this call
/// plans: the current on-air item's remaining time plus every feeder-pushed item still queued
/// behind it. This is the queue-lookahead drift the boundary-fit selector corrects for — a track
/// picked "due in 6 minutes" actually STARTS that much later. Additive and optional
/// (<see langword="null"/> = unknown, treated as zero — the pre-gh-#254 shape every existing
/// construction site keeps); components with no measured duration contribute nothing, so this is
/// always an honest floor, never a fabricated total.
/// </param>
public sealed record PlayoutContext(IReadOnlyList<string> RecentMediaIds, int? QueuedAheadMs = null);
