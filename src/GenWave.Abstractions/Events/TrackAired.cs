namespace GenWave.Core.Events;

/// <summary>
/// A new stamped item came on-air (real track-id advance detected by the feeder). Carries the same
/// Core-friendly primitives the retired <c>PlayoutFeeder.OnAdvance</c> single-cast callback did —
/// this event is that signal, multicast-capable (gitea-#246). <paramref name="DurationMs"/> is null for
/// engine-initiated advances and <c>tts:*</c> patter (SPEC F50.2, F50.6).
/// </summary>
public sealed record TrackAired(
    string MediaId,
    string? Title,
    string? Artist,
    double GainDb,
    DateTimeOffset StartedAt,
    int? DurationMs) : StationEvent;
