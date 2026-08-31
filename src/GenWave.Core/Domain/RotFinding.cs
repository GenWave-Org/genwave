namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>library.rot_finding</c> (SPEC F153.1, F153.9; STORY-374; PLAN T372,
/// gh-#529) — <see cref="Abstractions.IRotFindingStore.ListAsync"/>'s own element type. One row
/// exists per <c>(media_id, kind)</c> forever; only <see cref="State"/> and the timestamps below
/// move.
/// </summary>
/// <param name="Id">The row's own surrogate key — stable across every open/resolve/re-open cycle
/// (STORY-374 AC3: the same row is reused, never duplicated).</param>
/// <param name="MediaId">The <c>library.media</c> row this finding is about.</param>
/// <param name="Kind">Which rot family this is (SPEC F153.1).</param>
/// <param name="State">The finding's current lifecycle state (SPEC F153.2).</param>
/// <param name="GroupKey">Set only for <see cref="RotKind.NearDuplicate"/> findings (SPEC F153.1);
/// <see langword="null"/> for every other kind.</param>
/// <param name="Evidence">The raw <c>jsonb</c> text a pass wrote (e.g. <c>{"reason":"failed",
/// "since":"2026-08-01T00:00:00Z"}</c> for <see cref="RotKind.DeadFile"/>, SPEC F153.3) —
/// deliberately opaque here, the same <c>FontPack.Definition</c>/<c>OwnerTheme</c> precedent: a
/// caller downstream of this Core seam (T377's endpoint) reconstitutes the per-kind shape it
/// expects at its own edge.</param>
/// <param name="OpenedAt">When this row was first opened, or most recently re-opened from
/// <see cref="RotState.Resolved"/> — an already-<see cref="RotState.Open"/> row's own reconcile
/// never bumps this (F153.2's "as built" amendment).</param>
/// <param name="ResolvedAt">When the predicate last stopped holding; <see langword="null"/> unless
/// <see cref="State"/> is (or was) <see cref="RotState.Resolved"/>.</param>
/// <param name="DismissedAt">When the owner dismissed this finding; <see langword="null"/> unless
/// <see cref="State"/> is <see cref="RotState.Dismissed"/>.</param>
/// <param name="UpdatedAt">The row's own last-write stamp.</param>
public sealed record RotFinding(
    long Id,
    long MediaId,
    RotKind Kind,
    RotState State,
    string? GroupKey,
    string Evidence,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? DismissedAt,
    DateTimeOffset UpdatedAt);
