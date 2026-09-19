using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// The slot renders caller-authored copy through <c>IVerbatimSegmentRenderer</c> — no LLM, no copy
/// writer ever runs on this path (SPEC F144.2). <c>AnnouncementBrief</c> does not exist yet; this
/// carries exactly what <c>IVerbatimSegmentRenderer.RenderAsync(SegmentRequest, SegmentCopy, ct)</c>
/// needs today. SPEC F187.4 (PLAN T527): <see cref="Request"/> carries its own
/// <see cref="SegmentRequest.Speaker"/>, stamped by the plan phase, the same rule
/// <see cref="RenderSource"/> uses — <see langword="null"/> for a caller that passes no
/// <c>ISpeakerSnapshotSource</c> (SPEC F189.6).
/// </summary>
/// <param name="Request">The request the copy renders under.</param>
/// <param name="Copy">The already-decided text to render verbatim — an announcement's own plain fallback text, whether or not <see cref="AllowFlavor"/> is set.</param>
/// <param name="AllowFlavor">
/// When true, the render side resolves flavored copy through the announcement copy writer first and
/// falls back to <paramref name="Copy"/> on null/throw (SPEC F191.4); false renders <paramref name="Copy"/> verbatim.
/// </param>
public sealed record VerbatimSource(SegmentRequest Request, SegmentCopy Copy, bool AllowFlavor) : SlotSource;
