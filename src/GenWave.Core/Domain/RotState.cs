namespace GenWave.Core.Domain;

/// <summary>
/// A <see cref="RotFinding"/>'s lifecycle state (SPEC F153.1, F153.2; STORY-374; PLAN T372,
/// gh-#529) — maps 1:1 to <c>library.rot_state</c> by lowercase text, <c>Garden.RotFindingRepository</c>
/// owning the mapping. A pass <see cref="Open"/>s (or re-opens a <see cref="Resolved"/>) finding
/// when its predicate holds and <see cref="Resolved"/>s an <see cref="Open"/> one when it stops
/// holding; <see cref="Dismissed"/> is forever — no pass ever moves a row out of it.
/// </summary>
public enum RotState
{
    /// <summary>The predicate holds right now; the finding is live in the queue.</summary>
    Open,

    /// <summary>The station owner dismissed this finding at the store level (STORY-374 AC4); no
    /// pass ever re-opens it, regardless of what the predicate does next.</summary>
    Dismissed,

    /// <summary>The predicate held once and no longer does — the row self-healed. A pass re-opens
    /// it (back to <see cref="Open"/>) if the predicate ever holds again.</summary>
    Resolved,
}
