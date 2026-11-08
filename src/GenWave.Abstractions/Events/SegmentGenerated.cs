namespace GenWave.Core.Events;

/// <summary>
/// A TTS segment render succeeded (cache hit or fresh synthesis). <paramref name="SegmentId"/> is
/// the feeder-visible <c>tts:&lt;hash&gt;</c> id; <paramref name="Kind"/> is the segment kind name
/// (StationId, LeadIn, BackAnnounce, TimeDate, …).
/// </summary>
public sealed record SegmentGenerated(
    string SegmentId,
    string Kind,
    string Voice) : StationEvent
{
    /// <summary>SPEC F218.2 (STORY-532, PLAN T684) — persona the segment was written for, when known. Init-only so the published constructor is unchanged.</summary>
    public long? PersonaId { get; init; }

    /// <summary>SPEC F218.2 — the on-air show when the segment was planned; <see langword="null"/> when unscheduled.</summary>
    public long? ShowId { get; init; }

    /// <summary>SPEC F218.2 — the <c>station.utterance</c> row recorded for this copy, if any. Never added to the booth log (it stays narrative).</summary>
    public long? UtteranceId { get; init; }

    /// <summary>
    /// SPEC F218.2 — the spoken copy; the booth log quotes its first 80 characters. Published at render,
    /// just before the segment airs, so it is the text about to be spoken, not yet heard.
    /// </summary>
    public string? Text { get; init; }
}
