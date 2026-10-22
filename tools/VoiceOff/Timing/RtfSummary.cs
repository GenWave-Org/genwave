namespace GenWave.VoiceOff.Timing;

/// <summary>
/// Median and p95 real-time factor over successful renders (SPEC F214.7). Percentiles interpolate linearly
/// between the closest ranks, rank = p x (n - 1) (the numpy default).
/// </summary>
internal sealed record RtfSummary(double Median, double P95)
{
    /// <summary>Summarises the ok records that have an rtf; null when there are none.</summary>
    internal static RtfSummary? From(IEnumerable<TimingRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        double[] rtfs = records
            .Where(r => r.Ok)
            .Select(r => r.Rtf)
            .OfType<double>()
            .Order()
            .ToArray();
        return rtfs.Length == 0 ? null : new RtfSummary(Percentile(rtfs, 0.5), Percentile(rtfs, 0.95));
    }

    internal static double Percentile(double[] sorted, double p)
    {
        double rank = p * (sorted.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((rank - lower) * (sorted[upper] - sorted[lower]));
    }
}
