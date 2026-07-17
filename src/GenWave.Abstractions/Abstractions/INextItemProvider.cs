using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM 1 (PRD §4.1) — the feeder pulls the next track through this and nothing else. Deliberately
/// narrow: the feeder needs only what it takes to push a track and compute gain.
/// </summary>
public interface INextItemProvider
{
    /// <summary>
    /// The next track to prepare, or null when nothing is available right now (empty/cold library, or
    /// — once this is a remote call — a timeout). Null is NON-FATAL and part of the contract: the
    /// feeder retries next tick and the safe rotation covers the gap. NEVER throw across this seam for
    /// "nothing to play". Tolerant pull is written in now even though the v1 in-process call can't
    /// fail across a wire — it is the one thing that is annoying to retrofit (PRD §4.1).
    /// </summary>
    Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct);
}
