using System.Globalization;

namespace GenWave.VoiceOff.Render;

/// <summary>The one-line per-engine summary printed after its clips (SPEC F214.7).</summary>
internal static class EngineSummary
{
    private const string Missing = "—";
    private const string Fail = "❌";

    /// <summary>The row for an engine that was rendered; one that stopped answering starts the row with the mark and the reason.</summary>
    internal static string For(string slug, IReadOnlyList<TimingRecord> records, bool stoppedAnswering)
    {
        ArgumentNullException.ThrowIfNull(records);
        RtfSummary? rtf = RtfSummary.From(records);
        string median = rtf is null ? Missing : Number(rtf.Median);
        string p95 = rtf is null ? Missing : Number(rtf.P95);
        string stopped = stoppedAnswering ? $"{Fail} engine stopped answering, " : string.Empty;
        return $"{slug}: {stopped}{records.Count} clips, {records.Count(x => !x.Ok)} failed, rtf median {median} p95 {p95}";
    }

    /// <summary>The row for an engine that failed its container gate and was not rendered.</summary>
    internal static string ForFailedContainer(string slug, string reason) =>
        $"{slug}: {Fail} container: {reason}";

    internal static string ForNotAttempted(string slug, string reason) =>
        $"{slug}: not attempted ({reason})";

    internal static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
