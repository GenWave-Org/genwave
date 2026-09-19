using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// The slot renders a template segment through <c>ITtsSegmentSource</c> at render time (SPEC F186's
/// Render column). The speaker is read from <see cref="Request"/>'s own
/// <see cref="SegmentRequest.PersonaName"/> — <see langword="null"/> traces as the station itself.
/// <b>Interim (PLAN T520/T527):</b> once <c>SpeakerSnapshot</c> exists (SPEC F189), the plan phase
/// will capture it here instead of re-reading <see cref="SegmentRequest.PersonaName"/> at trace time.
/// </summary>
/// <param name="Request">The fully-built request the renderer will use.</param>
public sealed record RenderSource(SegmentRequest Request) : SlotSource;
