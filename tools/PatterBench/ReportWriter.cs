namespace GenWave.PatterBench;

using System.Globalization;
using System.Text;

/// <summary>Builds the Markdown report: a short header, then one table row per model and persona.</summary>
internal static class ReportWriter
{
    private const string NotAvailable = "n/a";

    internal static string Build(BenchArgs args, DateTimeOffset startedUtc, IReadOnlyList<BenchCell> cells)
    {
        var report = new StringBuilder();
        report.Append("# 🎙️ Patter bench\n\n");
        report.Append(CultureInfo.InvariantCulture, $"- Endpoint: {args.Endpoint}\n");
        report.Append(CultureInfo.InvariantCulture, $"- Breaks per row: {args.Breaks}\n");
        report.Append(CultureInfo.InvariantCulture, $"- Run at: {startedUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n\n");
        report.Append("| Model | Persona | Distinct 4-grams | Top repeats | Catchphrase rate | Gate pass | p50 ms | p95 ms | VRAM GiB |\n");
        report.Append("|---|---|---|---|---|---|---|---|---|\n");
        foreach (var cell in cells)
        {
            report.Append(Row(cell)).Append('\n');
        }

        return report.ToString();
    }

    private static string Row(BenchCell cell)
    {
        var copies = cell.Copies;
        var gatePass = cell.Latencies.Count == 0 ? (double?)null : (double)cell.GatePasses / cell.Latencies.Count;
        return string.Join(" | ",
            $"| {cell.Model}",
            cell.Persona,
            Fixed(BenchScore.DistinctFourGramRatio(copies), "F2"),
            TopRepeats(BenchScore.TopRepeats(copies)),
            Fixed(BenchScore.CatchphraseRate(copies, cell.Card.QuirkRules), "F2"),
            Fixed(gatePass, "F2"),
            Fixed(BenchScore.PercentileMs(cell.Latencies, 50), "F0"),
            Fixed(BenchScore.PercentileMs(cell.Latencies, 95), "F0"),
            $"{Fixed(cell.VramGiB, "F1")} |");
    }

    private static string Fixed(double? value, string format) =>
        value is { } number ? number.ToString(format, CultureInfo.InvariantCulture) : NotAvailable;

    private static string TopRepeats(IReadOnlyList<RepeatedPhrase> repeats) =>
        repeats.Count == 0
            ? "none"
            : string.Join("; ", repeats.Select(r => $"\"{r.Phrase}\" ×{r.Copies}"));
}
