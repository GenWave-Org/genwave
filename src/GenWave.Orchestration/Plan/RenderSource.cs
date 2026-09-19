using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// The slot renders a template segment through <c>ITtsSegmentSource</c> at render time (SPEC F186's
/// Render column). SPEC F187.4 (PLAN T527): <see cref="Request"/> carries its own
/// <see cref="SegmentRequest.Speaker"/>, stamped by the plan phase — <see langword="null"/> for a
/// caller that passes no <c>ISpeakerSnapshotSource</c> (SPEC F189.6), in which case the trace falls
/// back to <see cref="SegmentRequest.PersonaName"/>, <see langword="null"/> tracing as the station.
/// </summary>
/// <param name="Request">The fully-built request the renderer will use.</param>
public sealed record RenderSource(SegmentRequest Request) : SlotSource;
