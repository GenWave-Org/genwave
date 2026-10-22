using System.Globalization;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Render;

/// <summary>The one-line per-engine summary printed after its clips (SPEC F214.7).</summary>
internal static class EngineSummary
{
    private const string Missing = "—";
    private const string Pass = "✅";
    private const string Fail = "❌";

    /// <summary>The row for an engine that was rendered; a failed voiceSelect gate or an engine that stopped answering starts the row with the mark and the reason.</summary>
    internal static string For(EngineRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IReadOnlyList<TimingRecord> records = result.Records;
        RtfSummary? rtf = RtfSummary.From(records);
        string median = rtf is null ? Missing : Number(rtf.Median);
        string p95 = rtf is null ? Missing : Number(rtf.P95);
        string rss = result.Usage.PeakRssMb is long r ? $"{r} MB" : Missing;
        string vram = result.Usage.PeakVramMb is long v ? $"{v} MB" : Missing;
        string failures = Failures(result);
        return $"{result.Gates.Slug}: {failures}{records.Count} clips, {records.Count(x => !x.Ok)} failed, rtf median {median} p95 {p95}, "
            + $"peak RSS {rss}, VRAM {vram}, voiceSelect {Mark(result.Gates.VoiceSelect?.Passed)}, container {Mark(result.Gates.Container?.Passed)}";
    }

    // A rendered engine that stopped answering or failed a gate starts its row with the mark and says why (F214.6: never silently dropped).
    private static string Failures(EngineRunResult result)
    {
        List<string> reasons = [];
        if (result.StoppedAnswering)
        {
            reasons.Add("engine stopped answering");
        }

        if (result.Gates.VoiceSelect is { Passed: false } voiceSelect)
        {
            reasons.Add($"voiceSelect: {voiceSelect.Reason}");
        }

        return reasons.Count == 0 ? string.Empty : $"{Fail} {string.Join(", ", reasons)}, ";
    }

    /// <summary>The row for an engine that failed its container gate and was not rendered.</summary>
    internal static string ForFailedContainer(GateEntry gates) =>
        $"{gates.Slug}: {Fail} container: {gates.Container?.Reason}";

    internal static string ForNotAttempted(GateEntry gates) =>
        $"{gates.Slug}: not attempted ({gates.NotAttempted})";

    internal static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Mark(bool? passed) => passed is true ? Pass : Fail;
}
