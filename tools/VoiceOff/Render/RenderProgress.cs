using System.Globalization;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Render;

/// <summary>The progress line printed before each clip (STORY-494): where the run is, and once a clip is done, how long is left.</summary>
internal static class RenderProgress
{
    /// <param name="clipNumber">The clip about to start, counted from 1.</param>
    /// <param name="total">The clips the run still plans to render, including the ones done.</param>
    /// <param name="doneWallMs">Wall time of every real render finished so far.</param>
    internal static string Line(int clipNumber, int total, IReadOnlyList<double> doneWallMs)
    {
        ArgumentNullException.ThrowIfNull(doneWallMs);
        string head = string.Create(CultureInfo.InvariantCulture, $"clip {clipNumber}/{total}");
        if (doneWallMs.Count == 0)
        {
            return head;
        }

        double median = RtfSummary.Percentile(doneWallMs.Order().ToArray(), 0.5);
        int remaining = Math.Max(total - clipNumber + 1, 1);
        return string.Create(CultureInfo.InvariantCulture, $"{head} · ~{EtaMinutes(median, remaining):0} min left");
    }

    /// <summary>Whole minutes for <paramref name="clips"/> at a median of <paramref name="medianWallMs"/> each: rounded up, never under one.</summary>
    internal static double EtaMinutes(double medianWallMs, int clips) => Math.Max(Math.Ceiling(medianWallMs * clips / 60_000), 1);
}
