using System.Globalization;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Score;

/// <summary>
/// RESULTS.md (SPEC F214.14, F214.15): a pure function of the loaded runs and the profiles. Invariant culture, sorted rows, <c>\n</c> line endings,
/// no wall clock and no paths; the date is the run's own start date.
/// </summary>
internal static class ResultsMarkdown
{
    /// <summary>The tier scores in tier order (cpu before gpu).</summary>
    internal static IReadOnlyList<TierScore> Score(ScoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Runs.OrderBy(r => r.Tier).Select(run => TierScore.Build(input.Profiles, run)).ToArray();
    }

    /// <param name="scores">The result of <see cref="Score"/> for the same input.</param>
    internal static string Render(ScoreInput input, IReadOnlyList<TierScore> scores)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(scores);
        string baseline = input.Profiles.Baseline.Slug;
        List<string> lines =
        [
            "# 🎙️ Voice Off results",
            string.Empty,
            $"Every engine is compared with the baseline engine, {baseline}, in blind pairs. Speed never affects a verdict.",
        ];
        foreach (TierScore score in scores)
        {
            lines.AddRange(Section(score, input, baseline));
        }

        return string.Join("\n", lines) + "\n";
    }

    private static List<string> Section(TierScore score, ScoreInput input, string baseline)
    {
        TierRun run = score.Run;
        List<string> lines =
        [
            string.Empty,
            $"## 🖥️ Tier: {RunId.TierName(run.Tier)}",
            string.Empty,
            $"- Run: {run.RunId}",
            $"- Date: {run.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"- Host: {MarkdownCell.Escape(run.Host)}",
            $"- Tier: {RunId.TierName(run.Tier)}",
            $"- Gauntlet hash: {MarkdownCell.Escape(run.GauntletHash)}",
            string.Empty,
            $"**Verdict: {score.Verdict.Text}**",
            string.Empty,
            "### 🚦 Gates",
            string.Empty,
            .. ResultsTables.Gates(score, baseline),
            string.Empty,
            "### ⏱️ Scored",
            string.Empty,
            .. ResultsTables.Scored(score, input.Profiles),
            string.Empty,
            SpeedReference.Shipped(),
        ];
        if (input.BudgetSeconds is int budget)
        {
            lines.Add(string.Empty);
            lines.Add(SpeedReference.YourBox(budget));
        }

        foreach (RenderLane lane in Enum.GetValues<RenderLane>())
        {
            lines.Add(string.Empty);
            lines.Add($"### 🎧 Win rate, {lane.ToString().ToLowerInvariant()} lane");
            lines.Add(string.Empty);
            lines.AddRange(ResultsTables.Lane(score, lane, baseline));
        }

        lines.Add(string.Empty);
        lines.Add("### 😬 Wince counts");
        lines.Add(string.Empty);
        lines.AddRange(ResultsTables.Winces(score));
        return lines;
    }
}
