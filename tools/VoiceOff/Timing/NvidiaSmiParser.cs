using System.Globalization;

namespace GenWave.VoiceOff.Timing;

/// <summary>
/// Reads <c>nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits</c> (SPEC F214.7): MiB used, one line per GPU,
/// summed. VRAM for an engine is the GPU total's growth over a baseline taken before the engine loaded
/// (one engine on the GPU at a time).
/// </summary>
internal static class NvidiaSmiParser
{
    /// <summary>The MiB used across all GPUs, or null when any line is not a number (no GPU, driver error).</summary>
    internal static long? ParseUsedMib(string? output)
    {
        string[] lines = (output ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        long total = 0;
        foreach (string line in lines)
        {
            if (!long.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out long used))
            {
                return null;
            }

            total += used;
        }

        return total;
    }

    /// <summary>How far a peak sits above the baseline; never negative.</summary>
    internal static long DeltaMb(long baselineMib, long peakMib) => Megabytes.FromMib(Math.Max(0, peakMib - baselineMib));
}
