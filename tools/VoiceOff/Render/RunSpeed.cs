using System.Globalization;

namespace GenWave.VoiceOff.Render;

/// <summary>How fast a previous run rendered, or why that is not known: the basis of a dry run's ETA.</summary>
internal sealed record RunSpeed(string? RunId, double MedianWallMs, string? UnknownReason)
{
    internal static RunSpeed Known(string runId, double medianWallMs) => new(runId, medianWallMs, null);

    internal static RunSpeed Unknown(string reason) => new(null, 0, reason);

    /// <summary>The ETA line for <paramref name="clips"/> clips.</summary>
    internal string EtaLine(int clips) => RunId is null
        ? $"ETA unknown ({UnknownReason})"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"ETA ~{RenderProgress.EtaMinutes(MedianWallMs, clips):0} min (median {MedianWallMs / 1000:0.0} s per clip from run {RunId})");
}
