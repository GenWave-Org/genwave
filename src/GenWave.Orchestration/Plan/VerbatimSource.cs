using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// The slot renders caller-authored copy through <c>IVerbatimSegmentRenderer</c> — no LLM, no copy
/// writer ever runs on this path (SPEC F144.2). <c>AnnouncementBrief</c> does not exist yet; this
/// carries exactly what <c>IVerbatimSegmentRenderer.RenderAsync(SegmentRequest, SegmentCopy, ct)</c>
/// needs today. The speaker is read from <see cref="Request"/>'s own
/// <see cref="SegmentRequest.PersonaName"/>, the same interim rule <see cref="RenderSource"/> uses.
/// <b>Interim (PLAN T520/T527):</b> once <c>SpeakerSnapshot</c> exists (SPEC F189), the plan phase
/// will capture it here instead.
/// </summary>
/// <param name="Request">The request the copy renders under.</param>
/// <param name="Copy">The already-decided text to render verbatim.</param>
public sealed record VerbatimSource(SegmentRequest Request, SegmentCopy Copy) : SlotSource;
