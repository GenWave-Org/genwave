using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// gh-#253 — the planning-time seam for "how long will that patter run?": when the Orchestrator
/// plans upcoming items, the next break's patter has no known duration (the LLM copy is unwritten
/// and the TTS unrendered at pick time; measured duration exists only post-render, SPEC F66.1).
/// This seam exposes an honest ESTIMATE instead, tiered by <see cref="PatterEstimateConfidence"/>:
/// exact (already-rendered audio), historical (rolling average of aired patter), or heuristic
/// (chars-per-second cold fallback).
///
/// <para>
/// By itself this changes no behavior — it only exposes numbers. The gh-#254 boundary-fit selector
/// is the first consumer, widening its landing tolerance as the confidence tier drops.
/// </para>
///
/// <para>
/// Both members must be cheap and non-blocking (in-memory only, no I/O): <see cref="Estimate"/> is
/// called from the music-pick hot path, and <see cref="ObserveRendered"/> from the per-unit render
/// loop. Implementations must be thread-safe — the feeder tick and any future producer may overlap.
/// </para>
/// </summary>
public interface IPatterDurationEstimator
{
    /// <summary>
    /// Estimates the spoken duration of an upcoming segment of <paramref name="kind"/>, voiced by
    /// <paramref name="personaName"/> (<see langword="null"/> = the station itself, e.g. a station
    /// ID — gh-#96) with TTS voice <paramref name="voice"/>. Never throws, never returns a
    /// non-positive duration — an implementation with nothing better always has the heuristic floor.
    /// </summary>
    PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice);

    /// <summary>
    /// Feeds one real, MEASURED rendered duration back into the estimator (the F66.1 cue-derived
    /// <c>DurationMs</c> a completed render stamped) so the historical tier self-improves with every
    /// aired segment. Callers must never fabricate <paramref name="measured"/> — no estimate is ever
    /// observed back in.
    /// </summary>
    void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured);
}
