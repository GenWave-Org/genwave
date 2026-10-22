namespace GenWave.VoiceOff.Timing;

/// <summary>"MB" in this tool means MiB rounded to the nearest whole number; this is the one place that rounds.</summary>
internal static class Megabytes
{
    internal static long FromMib(double mib) => (long)Math.Round(mib, MidpointRounding.AwayFromZero);
}
