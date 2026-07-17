namespace GenWave.Core.Events;

/// <summary>
/// A TTS segment render succeeded (cache hit or fresh synthesis). <paramref name="SegmentId"/> is
/// the feeder-visible <c>tts:&lt;hash&gt;</c> id; <paramref name="Kind"/> is the segment kind name
/// (StationId, LeadIn, BackAnnounce, TimeDate, …).
/// </summary>
public sealed record SegmentGenerated(
    string SegmentId,
    string Kind,
    string Voice) : StationEvent;
