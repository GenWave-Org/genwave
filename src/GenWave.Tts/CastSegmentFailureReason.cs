namespace GenWave.Tts;

/// <summary>
/// Which stage of <see cref="CastSegmentAuthor.AuthorAsync"/> aborted (SPEC F161.2, F161.3; STORY-391;
/// PLAN T401) — the <see cref="SafeSegmentFailureReason"/> shape one sibling over, widened for the
/// cast path's own extra confirmation stage.
/// </summary>
public enum CastSegmentFailureReason
{
    /// <summary>An exception escaped <see cref="CrosstalkAssembler.AssembleCastAsync"/> itself
    /// (synthesis, mix, or measurement) rather than resolving to a business
    /// <see cref="CrosstalkAssemblyResult.Discarded"/> (see the next value).</summary>
    AssemblyFailed,

    /// <summary><see cref="CrosstalkAssembler.AssembleCastAsync"/> returned a business
    /// <see cref="CrosstalkAssemblyResult.Discarded"/> — a per-line render failure (F99's right-voice
    /// bar) or an over-ceiling artifact (SPEC F161.2, never trimmed).</summary>
    Discarded,

    /// <summary><see cref="GenWave.Core.Abstractions.IAuthoredCatalogWriter.InsertAuthoredAsync"/>
    /// threw — the assembled artifact is deleted, nothing was written.</summary>
    InsertFailed,

    /// <summary>
    /// The row landed (ineligible, SPEC F161.3) but the caller-supplied confirmation delegate
    /// reported (or threw) failure — the caller's own bookkeeping never caught up, so the row stays
    /// ineligible rather than an AIRABLE orphan. Not a novel debris class (T401 review F4): SPEC
    /// F159.3 already ships this exact <c>eligible=false</c>, never-deleted shape for a
    /// routinely-retired ready spot's own media row, and it is not truly permanent either — an
    /// operator can flip <c>Eligible</c> back by hand via <c>PATCH /api/media/{id}</c> if a specific
    /// row is worth reclaiming. See <see cref="CastSegmentAuthor"/>'s own remarks for the retry
    /// consequence (F6, gh-#3's GC class) this residue leaves for a future task.
    /// </summary>
    ConfirmationFailed,
}
