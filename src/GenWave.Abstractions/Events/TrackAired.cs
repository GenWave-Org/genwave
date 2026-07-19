namespace GenWave.Core.Events;

/// <summary>
/// A new stamped item came on-air (real track-id advance detected by the feeder). Carries the same
/// Core-friendly primitives the retired <c>PlayoutFeeder.OnAdvance</c> single-cast callback did —
/// this event is that signal, multicast-capable (gitea-#246). <paramref name="DurationMs"/> carries
/// <c>tts:*</c> patter's measured cue-derived duration (SPEC F66.1); it is null only for an
/// engine-initiated advance, which the Host rehydrates from the catalog after publish (SPEC F66.2).
/// </summary>
public sealed record TrackAired(
    string MediaId,
    string? Title,
    string? Artist,
    double GainDb,
    DateTimeOffset StartedAt,
    int? DurationMs) : StationEvent;
