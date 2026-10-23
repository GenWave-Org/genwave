using System.Globalization;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Score;

/// <summary>The tables of one tier's section of RESULTS.md. Pure; every list is sorted so the same input prints the same bytes.</summary>
internal static class ResultsTables
{
    private const string NoPairs = "no pairs";
    private const string NotRendered = "not rendered";

    internal static IEnumerable<string> Gates(TierScore score, string baseline)
    {
        yield return MarkdownCell.Row(["Engine", "Container", "Voice select"]);
        yield return MarkdownCell.Divider(3);
        foreach (GateEntry entry in OrderedGates(score.Run.Gates, baseline))
        {
            string notRun = entry.Attempted ? "— not run" : $"— not attempted: {entry.NotAttempted}";
            yield return MarkdownCell.Row([
                MarkdownCell.Escape(entry.Slug),
                MarkdownCell.Escape(GateCell(entry.Container, notRun)),
                MarkdownCell.Escape(GateCell(entry.VoiceSelect, notRun))]);
        }
    }

    internal static IEnumerable<string> Scored(TierScore score, EngineProfileSet profiles)
    {
        yield return MarkdownCell.Row(["Engine", "Median rtf", "p95 rtf", "Peak RSS (MB)", "Peak VRAM (MB)", "License"]);
        yield return MarkdownCell.Divider(6);
        foreach (GateEntry entry in OrderedGates(score.Run.Gates, profiles.Baseline.Slug))
        {
            string license = profiles.Profiles.First(p => string.Equals(p.Slug, entry.Slug, StringComparison.Ordinal)).License;
            yield return ScoredRow(score, entry.Slug, license);
        }
    }

    internal static IEnumerable<string> Lane(TierScore score, RenderLane lane, string baseline)
    {
        yield return MarkdownCell.Row(["Engine", "Wins", "Losses", "Ties", "n", "Win rate", .. score.Matrix.Judges.Select(MarkdownCell.Escape), "Note"]);
        yield return MarkdownCell.Divider(7 + score.Matrix.Judges.Count);
        foreach (string engine in LaneEngines(score, baseline))
        {
            if (!score.Matrix.Engines.Contains(engine, StringComparer.Ordinal))
            {
                yield return MarkdownCell.Row([MarkdownCell.Escape(engine), .. Enumerable.Repeat(Rate.Missing, 5 + score.Matrix.Judges.Count), NoPairs]);
                continue;
            }

            yield return LaneRow(score, score.Matrix.Find(engine, lane), score.Matrix.Find(engine, RenderLane.Plain), score.Matrix.Find(engine, RenderLane.Markup));
        }
    }

    internal static IEnumerable<string> Winces(TierScore score)
    {
        IReadOnlyDictionary<string, int> counted = TierScore.CountedWinces(score.Run);
        if (score.Run.Manifest.Reels.Count == 0)
        {
            yield return "No reels in this run.";
            yield break;
        }

        yield return MarkdownCell.Row(["Reel engine", "Winces"]);
        yield return MarkdownCell.Divider(2);
        foreach (ManifestReel reel in score.Run.Manifest.Reels.OrderBy(r => r.Engine, StringComparer.Ordinal))
        {
            string count = counted.TryGetValue(reel.Engine, out int value) ? value.ToString(CultureInfo.InvariantCulture) : "not counted";
            yield return MarkdownCell.Row([MarkdownCell.Escape(reel.Engine), count]);
        }
    }

    private static string ScoredRow(TierScore score, string slug, string license)
    {
        TimingRecord[] records = score.Run.Timing.Where(r => string.Equals(r.Engine, slug, StringComparison.Ordinal)).ToArray();
        RtfSummary? rtf = RtfSummary.From(records);
        bool gpu = score.Run.Tier == EngineTier.Gpu;
        return MarkdownCell.Row([
            MarkdownCell.Escape(slug),
            rtf is null ? NotRendered : Number(rtf.Median, "0.###"),
            rtf is null ? Rate.Missing : Number(rtf.P95, "0.###"),
            Peak(records.Select(r => r.RssMb)),
            gpu ? Peak(records.Select(r => r.VramMb)) : Rate.Missing,
            MarkdownCell.Escape(license)]);
    }

    private static string LaneRow(TierScore score, Cell cell, Cell plain, Cell markup)
    {
        Tally total = cell.Total;
        string?[] notes =
        [
            MarginRule.Evaluate(cell).InsufficientText,
            MarginRule.NeedsCueMapper(plain, markup) ? MarginRule.CueMapperRider : null,
        ];
        return MarkdownCell.Row([
            MarkdownCell.Escape(cell.Engine),
            Count(total.Wins),
            Count(total.Losses),
            Count(total.Ties),
            Count(total.N),
            Rate.Format(total.Rate),
            .. score.Matrix.Judges.Select(judge => JudgeCell(cell, judge)),
            string.Join("; ", notes.OfType<string>())]);
    }

    private static string JudgeCell(Cell cell, string judge)
    {
        Tally own = cell.PerJudge.GetValueOrDefault(judge) ?? Tally.Empty;
        return $"{Rate.Format(own.Rate)} ({Count(own.Wins)}/{Count(own.Losses)})";
    }

    /// <summary>The baseline first, then the rest in slug order.</summary>
    private static IEnumerable<GateEntry> OrderedGates(IReadOnlyList<GateEntry> gates, string baseline) => gates
        .OrderBy(g => string.Equals(g.Slug, baseline, StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(g => g.Slug, StringComparer.Ordinal);

    private static IEnumerable<string> LaneEngines(TierScore score, string baseline) => score.Run.Gates
        .Select(g => g.Slug)
        .Concat(score.Matrix.Engines)
        .Where(slug => !string.Equals(slug, baseline, StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal);

    private static string GateCell(GateResult? result, string notRun) =>
        result is null ? notRun : (result.Passed ? "✅ " : "❌ ") + result.Reason;

    private static string Peak(IEnumerable<long?> values)
    {
        long[] known = values.OfType<long>().ToArray();
        return known.Length == 0 ? Rate.Missing : known.Max().ToString(CultureInfo.InvariantCulture);
    }

    private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
