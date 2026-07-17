namespace GenWave.Loudness;

/// <summary>
/// Configuration for silence-based cue-point detection (config section "Library:CueDetection").
/// Defaults match Liquidsoap's blank.eat threshold so offline trim and live backstop behave consistently
/// (SPEC F13.7).
/// </summary>
public sealed class CueDetectionOptions
{
    public const string Section = "Library:CueDetection";

    /// <summary>Noise floor threshold in dB for silence detection. Default -50.0 dB.</summary>
    public double SilenceThresholdDb { get; set; } = -50.0;

    /// <summary>Minimum duration in seconds for a region to be considered silence. Default 0.5 s.</summary>
    public double MinSilenceDurationSec { get; set; } = 0.5;

    /// <summary>Maximum number of backfill rows processed per enrichment tick. Default 50.</summary>
    public int BackfillBatchSize { get; set; } = 50;
}
