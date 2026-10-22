using System.Globalization;

namespace GenWave.VoiceOff.Timing;

/// <summary>
/// Reads the two texts that give an engine container's own GPU memory (SPEC F214.7): the container's host PIDs from
/// <c>docker top &lt;container&gt; -eo pid</c>, and <c>nvidia-smi --query-compute-apps=pid,used_memory --format=csv,noheader,nounits</c>
/// (MiB per process). VRAM for an engine is the sum over the processes that belong to its container; other processes are ignored.
/// </summary>
internal static class NvidiaSmiParser
{
    private const string PidHeader = "PID";

    /// <summary>The PIDs under the <c>PID</c> header; null when the text has no PID or any line is not a number.</summary>
    internal static IReadOnlySet<int>? ParsePids(string? output)
    {
        HashSet<int> pids = [];
        foreach (string line in Lines(output).Where(l => !string.Equals(l, PidHeader, StringComparison.OrdinalIgnoreCase)))
        {
            if (!int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            {
                return null;
            }

            pids.Add(pid);
        }

        return pids.Count == 0 ? null : pids;
    }

    /// <summary>
    /// The MiB used by <paramref name="pids"/> across the <c>pid, used_memory</c> lines. Null when the text is missing or any
    /// line is not two numbers (no GPU, a driver error, <c>[N/A]</c>). No process of the container on the GPU is 0.
    /// </summary>
    internal static long? ParseContainerUsedMib(string? output, IReadOnlySet<int> pids)
    {
        ArgumentNullException.ThrowIfNull(pids);
        if (output is null)
        {
            return null;
        }

        long total = 0;
        foreach (string line in Lines(output))
        {
            string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long used))
            {
                return null;
            }

            if (pids.Contains(pid))
            {
                total += used;
            }
        }

        return total;
    }

    private static string[] Lines(string? output) =>
        (output ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
