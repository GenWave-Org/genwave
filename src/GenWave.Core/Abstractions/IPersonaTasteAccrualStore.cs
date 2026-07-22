using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F84.1-F84.6; STORY-215, PLAN T70) — the ONLY write path onto an accrued artist-taste
/// rule. Deliberately its OWN interface, never a widening of <see cref="IPersonaTasteStore"/> or
/// <see cref="IPersonaTasteReader"/>: the nudge (read the current weight, step it ±0.2, clamp to
/// <c>[-1, 1]</c>, write it back) plus the cap-50-weakest-evicted sweep (F84.3) and the
/// (persona, airing, direction) idempotency ledger (F84.5) all have to run as ONE transaction
/// against ONE connection — see <c>GenWave.MediaLibrary.Station.PersonaTasteAccrualRepository</c>'s
/// own remarks for the mechanism (the CARRIED T59 review note this closes). <c>GenWave.Orchestration.
/// PersonaRanker</c> (SPEC F84.2 structural) never sees this seam at all: it depends only on
/// <see cref="IPersonaTasteReader"/>, which has no member this interface could even be confused with.
/// </summary>
public interface IPersonaTasteAccrualStore
{
    /// <summary>
    /// Nudges (or creates) the accrued artist rule for whichever persona was stamped on booth-log row
    /// <paramref name="boothLogId"/> at air time (F84.1, F84.6) — never whichever persona happens to
    /// be active NOW. One route shape serves both the now-playing and booth-log admin surfaces
    /// (STORY-215): the now-playing view resolves to its own latest track-start booth-log row and
    /// calls this exact same method. See <see cref="TasteThumbOutcome"/> for every possible result.
    /// </summary>
    Task<TasteThumbOutcome> ThumbAsync(long boothLogId, TasteThumbDirection direction, CancellationToken ct);
}
