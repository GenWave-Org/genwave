namespace GenWave.Core.Domain;

/// <summary>
/// One provider's segment-lane payload, already selected and joined at vend time (SPEC
/// F107.3/F125.2/F125.3) — <c>GenWave.Context.ContextPipeline.TickAsync</c>'s own per-slot output for
/// a due provider, and what <c>GenWave.Orchestration.SpeechDeferral.Context</c> carries from enqueue
/// to drain. Deliberately minimal and internal to the repo, not part of the published MIT
/// <c>GenWave.Abstractions</c> surface (F105.6, the same posture <see cref="ContextPatterFact"/>
/// documents) — nothing outside this codebase's own segment lane consumes it.
///
/// <para>
/// Lives in Core, not <c>GenWave.Context</c> (the same reason <see cref="ContextPatterFact"/> does):
/// <c>GenWave.Orchestration</c> references <c>GenWave.Core</c> alone, never
/// <c>GenWave.Context</c> — this type is what lets <c>SpeechDeferral</c> carry a context provider's
/// already-resolved segment text across that boundary with no project reference added.
/// </para>
///
/// <para>
/// <b>Already joined, never re-derived.</b> <see cref="SegmentFacts"/> is the pipeline's own
/// window-rotated join of <see cref="ContextContent.Facts"/> (F125.3: up to a fixed window of facts,
/// wrapping once the window runs past the list's end) — the Orchestrator's drain arm reads it
/// verbatim into <c>SegmentRequest.ContextFacts</c>, it never re-selects or re-joins anything itself.
/// </para>
/// </summary>
/// <param name="SegmentFacts">The already-selected, already-joined segment text for this vend — never
/// blank (an empty selection means "no segment lane this vend," which the pipeline represents by
/// producing no <see cref="ContextSegmentFacts"/> at all, not one with a blank
/// <see cref="SegmentFacts"/>).</param>
/// <param name="FreshUntil"><see cref="ContextContent.FreshUntil"/> verbatim, captured at enqueue
/// time — the Orchestrator's drain arm re-checks it against the drain-time clock (SPEC F107.3/F107.6)
/// since a unit boundary can land well after this was enqueued.</param>
public sealed record ContextSegmentFacts(string SegmentFacts, DateTimeOffset FreshUntil);
