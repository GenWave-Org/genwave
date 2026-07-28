using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F95.3, F95.5, STORY-251, PLAN T115) — the operator's explicit-classification override
/// on a catalog row: set true/false (stamps <c>explicit_source = 'operator'</c>, the top of F95.3's
/// precedence — operator &gt; tag &gt; llm — so it beats any tag/LLM classification unconditionally
/// and is never overwritten by a later sweep), or clear back to unknown (wipes <c>explicit</c>,
/// <c>explicit_source</c>, AND the LLM sweep's own re-claim gate <c>explicit_llm_missed_at</c> back
/// to <see langword="null"/>, releasing the row so a later tag/LLM pass may reclassify it). Every
/// method operates on any catalog row by id with no <see cref="LibraryScope"/> gating — the same
/// F33.5 rationale <see cref="IMediaRating"/> documents: classification is a per-row curation
/// property, not a rotation-scope one. Kept separate from <see cref="IAdminMediaWrite"/> so this
/// single method costs zero blast radius on every existing <c>IAdminMediaWrite</c> test double —
/// the same interface-segregation reasoning <see cref="IAdminMediaWrite"/>'s own doc comment states
/// for splitting query/lookup/write. F95.5's never-play orthogonality needs no code here at all: it
/// lives entirely in a separate table (<c>library.media_rating</c>) and a separate WHERE conjunct
/// this seam never touches.
/// </summary>
public interface IMediaExplicitOverride
{
    /// <summary>
    /// Sets or clears the operator's explicit-classification override, atomically in a single
    /// statement (SPEC F95.3). <paramref name="explicitValue"/> <see langword="null"/> clears the
    /// row back to unknown — <c>explicit</c>, <c>explicit_source</c>, AND
    /// <c>explicit_llm_missed_at</c> are all wiped together, so the LLM sweep can re-ask.
    /// <see langword="true"/>/<see langword="false"/> stamps <c>explicit_source = 'operator'</c>
    /// unconditionally — an operator write beats everything by definition, so there is nothing to
    /// defer to (unlike the tag pass and the LLM sweep, which both check for and preserve an
    /// existing operator stamp).
    /// </summary>
    /// <param name="mediaId">The media row id.</param>
    /// <param name="explicitValue">
    /// The operator's verdict, or <see langword="null"/> to clear the override back to unknown.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ExplicitOverrideOutcome> SetExplicitOverrideAsync(long mediaId, bool? explicitValue, CancellationToken ct);
}
