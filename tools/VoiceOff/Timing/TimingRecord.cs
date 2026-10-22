using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Timing;

/// <summary>
/// One line of <c>timing.jsonl</c> (ARCHITECTURE "Render record"): how long one render took against how much audio it made.
/// <see cref="Linear"/> records whether the loudness match stayed linear (T609's logged decision).
/// </summary>
internal sealed record TimingRecord(
    string Engine,
    RenderLane Lane,
    string Line,
    EngineTier Tier,
    double WallMs,
    double AudioMs,
    long? RssMb,
    long? VramMb,
    bool Ok,
    string? Error,
    bool? Linear = null)
{
    /// <summary>Wall-clock over audio (SPEC F214.7). Null when there is no audio, never infinity.</summary>
    public double? Rtf => AudioMs > 0 ? WallMs / AudioMs : null;
}
