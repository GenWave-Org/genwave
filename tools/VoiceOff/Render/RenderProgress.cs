using System.Globalization;

namespace GenWave.VoiceOff.Render;

/// <summary>The progress line printed before each paragraph (STORY-494): where the run is, and once a paragraph is done, how long is left.</summary>
internal static class RenderProgress
{
    /// <param name="paragraphNumber">The paragraph about to start, counted from 1.</param>
    /// <param name="total">The paragraphs the run still plans to render, including the ones done.</param>
    /// <param name="doneWallMs">Wall time of every paragraph finished so far.</param>
    internal static string Line(int paragraphNumber, int total, IReadOnlyList<double> doneWallMs)
    {
        ArgumentNullException.ThrowIfNull(doneWallMs);
        string head = string.Create(CultureInfo.InvariantCulture, $"paragraph {paragraphNumber}/{total}");
        if (doneWallMs.Count == 0)
        {
            return head;
        }

        double median = Median(doneWallMs);
        int remaining = Math.Max(total - paragraphNumber + 1, 1);
        return string.Create(CultureInfo.InvariantCulture, $"{head} · ~{EtaMinutes(median, remaining):0} min left");
    }

    /// <summary>Whole minutes for <paramref name="paragraphs"/> at a median of <paramref name="medianWallMs"/> each: rounded up, never under one.</summary>
    internal static double EtaMinutes(double medianWallMs, int paragraphs) => Math.Max(Math.Ceiling(medianWallMs * paragraphs / 60_000), 1);

    /// <summary>The middle value of <paramref name="values"/> (the mean of the middle two when even); they must not be empty.</summary>
    internal static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = values.Order().ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
