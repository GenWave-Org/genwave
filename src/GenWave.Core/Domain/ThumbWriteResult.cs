namespace GenWave.Core.Domain;

/// <summary>
/// Closed outcome of <see cref="Abstractions.IThumbStore.RecordAsync"/> (SPEC F150.7; STORY-371,
/// STORY-369; PLAN T365) — mirrors <c>RatingWriteResult</c>'s own "small closed set of write
/// outcomes" shape one seam over. A Host controller (T366) collapses every member here to the SAME
/// constant 202 on the wire — <see cref="Ignored"/> in particular must never distinguish a
/// safe-scope row from an unknown media id in the HTTP response, or the route would leak library
/// topology to an anonymous spectator.
/// </summary>
public enum ThumbWriteResult
{
    /// <summary>The first thumb for this (media, airing, listener) triple — a new
    /// <c>library.media_thumb</c> row was inserted and the aggregate re-computed.</summary>
    Recorded,

    /// <summary>A repeat of the SAME direction for this (media, airing, listener) triple (F150.7's
    /// own idempotency): no row changed, no counter changed, no re-aggregation ran.</summary>
    Unchanged,

    /// <summary>The direction for this (media, airing, listener) triple changed (up→down or
    /// down→up): the existing row's <c>direction</c> was updated in place — never a second row —
    /// the new direction's lifetime counter was bumped, and the aggregate was re-computed.</summary>
    Flipped,

    /// <summary>Nothing was written: <paramref name="mediaId"/> (see
    /// <see cref="Abstractions.IThumbStore.RecordAsync"/>) is either an unknown
    /// <c>library.media</c> row or lives in a <c>Station:SafeScope:LibraryIds</c> library
    /// (gh-#99) — a thumb on functional audio is never meaningful. The caller must treat this
    /// identically to <see cref="Recorded"/>/<see cref="Flipped"/> on the wire (the constant 202).</summary>
    Ignored,
}
