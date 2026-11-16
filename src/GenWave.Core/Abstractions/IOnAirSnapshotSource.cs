using GenWave.Abstractions.Playout;

namespace GenWave.Core.Abstractions;

/// <summary>
/// A synchronous, never-throwing read of who and what is on the air right now (SPEC F218.15) — the answer
/// the schedule resolver last computed, for a consumer on a hot path (an <see cref="IStationEventSink"/>)
/// that cannot await a schedule load. <see langword="null"/> before the first schedule resolve completes.
/// </summary>
public interface IOnAirSnapshotSource
{
    /// <summary>The current snapshot, or <see langword="null"/> when none has been resolved yet.</summary>
    OnAirSnapshot? Current { get; }
}
