namespace GenWave.Loudness;

/// <summary>
/// Configuration for cue-window energy analysis (config section "Library:Energy").
/// </summary>
public sealed class EnergyOptions
{
    public const string Section = "Library:Energy";

    /// <summary>
    /// Duration in seconds of the intro and outro measurement windows.
    /// The intro window is [cueInSec, cueInSec + WindowSeconds]; the outro window is
    /// [cueOutSec - WindowSeconds, cueOutSec]. Default 12.0 s.
    /// </summary>
    public double WindowSeconds { get; set; } = 12.0;
}
