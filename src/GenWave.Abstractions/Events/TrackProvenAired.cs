namespace GenWave.Core.Events;

/// <summary>
/// A pushed item is now PROVEN to have aired although its own advance was never individually
/// observed: a short TTS segment can start and end between two feeder polls, so no
/// <see cref="TrackAired"/> exists for it, but a later sibling in push order was observed, and the
/// engine plays a pushed queue strictly in FIFO order. Deliberately a separate event: publishing a
/// second <see cref="TrackAired"/> would corrupt the booth log and play history, which depend on
/// <see cref="TrackAired"/> meaning "this item started now".
/// </summary>
/// <param name="MediaId">The id of the item proven to have aired (never the observed one).</param>
/// <param name="ProvenAt">When the airing was proven, not when it started.</param>
public sealed record TrackProvenAired(string MediaId, DateTimeOffset ProvenAt) : StationEvent;
