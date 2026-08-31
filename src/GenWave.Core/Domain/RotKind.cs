namespace GenWave.Core.Domain;

/// <summary>
/// The Library Gardener's finding kinds (SPEC F153.1; STORY-374, STORY-375; PLAN T372, gh-#529) —
/// maps 1:1 to <c>library.rot_kind</c> by lowercase-with-underscores text, the same explicit
/// enum-to-text discipline <see cref="ThumbDirection"/>/<c>VoteDirection</c> already establish for
/// their own Postgres enums in this codebase; <c>Garden.RotFindingRepository</c> owns that mapping.
/// One row per <c>(media_id, kind)</c> forever — <see cref="RotState"/> moves, this value never
/// changes for an existing row.
/// </summary>
public enum RotKind
{
    /// <summary>The file behind a catalog row is gone or unreadable (SPEC F153.3, PLAN T372): a
    /// <c>failed</c> state, a long-<c>unavailable</c> state, or a push-guard report (T373).</summary>
    DeadFile,

    /// <summary>Two or more playable rows fold to the same (artist, title) key within a duration
    /// tolerance (SPEC F153.5) — a later pass, not built at T372.</summary>
    NearDuplicate,

    /// <summary>A playable row is missing tags a listener would notice (SPEC F153.6) — a later
    /// pass, not built at T372.</summary>
    StaleMetadata,

    /// <summary>A playable row has sat unaired well past <c>Gardener:ShelfDustDays</c> (SPEC
    /// F153.7) — a later pass, not built at T372.</summary>
    ShelfDust,

    /// <summary>A playable row is admitted by no weekly-grid envelope (SPEC F153.8) — a later
    /// pass, not built at T372.</summary>
    Unreachable,
}
