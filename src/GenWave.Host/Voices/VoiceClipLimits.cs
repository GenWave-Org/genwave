namespace GenWave.Host.Voices;

/// <summary>The numbers a clip upload is held to (SPEC F219.12). The duration bounds mirror <c>station.voice_clip</c>'s own CHECK (db/53).</summary>
internal static class VoiceClipLimits
{
    public const int MinDurationMs = 5_000;
    public const int MaxDurationMs = 20_000;

    /// <summary>The largest upload accepted, in bytes (10 MiB).</summary>
    public const int MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>Headroom over <see cref="MaxFileBytes"/> for multipart framing, so an oversize file gets the 400 with a reason.</summary>
    public const int MaxRequestBytes = MaxFileBytes + 1024 * 1024;

    public const int SampleRateHz = 24_000;
    public const double TargetLufs = -20.0;
    public const int MaxLabelLength = 80;

    /// <summary>
    /// How much of the input is decoded. Anything longer than <see cref="MaxDurationMs"/> is refused, so reading a little past
    /// it is enough to know, and a long file never costs a long decode.
    /// </summary>
    public const int DecodeCapSeconds = 25;
}
