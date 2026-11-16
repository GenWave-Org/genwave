namespace GenWave.Core.Domain;

/// <summary>
/// The two distinct personas a <see cref="SegmentKind.SignOff"/>/<see cref="SegmentKind.SignOn"/> handoff
/// passes between (SPEC F218.15(b)): who handed over and who took over, as <see cref="CrosstalkParticipant"/>s so
/// the memory consumer can write an episode on each side from the aired item alone. Stamped at plan time on
/// <see cref="SegmentRequest.Handoff"/>, carried through <see cref="MediaItem.Handoff"/> to
/// <c>TrackAired.Handoff</c>. Only ever built when both sides are known and differ.
/// </summary>
/// <param name="Outgoing">The persona going off.</param>
/// <param name="Incoming">The persona coming on.</param>
/// <param name="BoundaryAt">The show boundary this handoff crosses; both halves are armed from the same instant, so it identifies the handoff however late a held SignOn airs.</param>
public sealed record HandoffParticipants(CrosstalkParticipant Outgoing, CrosstalkParticipant Incoming, DateTimeOffset BoundaryAt);
