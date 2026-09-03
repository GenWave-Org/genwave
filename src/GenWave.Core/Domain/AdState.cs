namespace GenWave.Core.Domain;

/// <summary>
/// The ad spot's total lifecycle state machine (SPEC F159.2; STORY-389; PLAN T398) — maps 1:1 to
/// <c>station.ad_state</c> by lowercase text, <see cref="AdStateTokens"/> owning the mapping (the
/// <c>RotKindTokens</c>/<c>RotStateTokens</c> precedent, PLAN T377: one enum-to-token map, not five
/// independent copies). Every transition below is enforced at the store
/// (<c>Abstractions.IAdSpotStore</c>), never in a caller — nothing here is ever deleted,
/// <see cref="Retired"/> is terminal.
/// </summary>
public enum AdState
{
    /// <summary>Newly generated or authored, awaiting the owner's own approval — the default (SPEC
    /// F159.4: off by default, so a generated spot always lands here first).</summary>
    Draft,

    /// <summary>Cleared to render — reached from <see cref="Draft"/> (operator, or automatic under
    /// <c>Station:Ads:AutoApprove</c>) or from <see cref="Failed"/> (an operator retry).</summary>
    Approved,

    /// <summary>Claimed by the render worker (PLAN T402's own claim stamp) — in flight toward
    /// <see cref="Ready"/> or <see cref="Failed"/>.</summary>
    Rendering,

    /// <summary>Rendered and air-eligible — the only state carrying a non-null <c>media_id</c> (SPEC
    /// F159.2, enforced by the store and by <c>station.ad_spot</c>'s own <c>CHECK</c>, db/43).</summary>
    Ready,

    /// <summary>A render (or, per STORY-390 AC3, a script that never passed validation) that did not
    /// produce an airable spot — <c>fail_reason</c> is always set here, and only here (db/43's own
    /// <c>CHECK</c>). Recoverable: an operator retry moves it back to <see cref="Approved"/>.</summary>
    Failed,

    /// <summary>Terminal — reached from <see cref="Ready"/> (refresh or operator) or
    /// <see cref="Draft"/> (operator discard). Never deleted, never re-entered.</summary>
    Retired,
}
