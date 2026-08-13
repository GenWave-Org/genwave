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
/// Both members must be cheap and non-blocking (in-memory only, no I/O): <see cref="Estimate(SegmentKind,string?,string)"/> is
/// called from the music-pick hot path, and <see cref="ObserveRendered(SegmentKind,string?,string,TimeSpan)"/> from the per-unit render
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

    /// <summary>
    /// SPEC F117.2 (STORY-309, PLAN T250 review finding F1) — <see cref="Estimate(SegmentKind,string?,string)"/>'s
    /// show-aware sibling: a genuinely NEW interface member, never that one widened in place (the SAME
    /// binary-compat/orphaned-implementer reasoning that governs every other addition to this published
    /// <c>GenWave.Abstractions</c> contract — see <see cref="IMediaCatalog"/>'s own F117 addition for
    /// the fuller rationale). <paramref name="showName"/> lets an implementation
    /// key its Exact tier on the RENDERED-TEXT identity for a <see cref="SegmentKind.StationId"/> segment
    /// whose copy now varies by on-air show (F117.2's templated show line — "You're listening to
    /// {show} on {station}." — is a DIFFERENT clip than the plain "You're listening to {station}."), so
    /// a show-branded observation can never be reported back as the Exact duration for an unrelated
    /// plain (or differently-shown) airing.
    /// <para>
    /// Default-implemented so this stays strictly additive: any implementer that has not opted in
    /// (only <c>RollingPatterDurationEstimator</c> does, today) degrades to the 3-arg overload with
    /// <paramref name="showName"/> simply dropped — exactly its own pre-F117 behavior, unchanged.
    /// </para>
    /// </summary>
    PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice, string? showName) =>
        Estimate(kind, personaName, voice);

    /// <summary>
    /// SPEC F117.2 (STORY-309, PLAN T250 review finding F1) — <see cref="ObserveRendered(SegmentKind,string?,string,TimeSpan)"/>'s
    /// show-aware sibling, the write-side counterpart to <see cref="Estimate(SegmentKind,string?,string,string?)"/>
    /// above; see that member's own remarks for why this is additive rather than an in-place widening,
    /// and for what <paramref name="showName"/> means. Default-implemented the same way: an implementer
    /// that has not opted in degrades to the 4-arg overload with <paramref name="showName"/> dropped.
    /// </summary>
    void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured, string? showName) =>
        ObserveRendered(kind, personaName, voice, measured);
}
