using System.Globalization;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Score;

/// <summary>
/// A tier's verdict (SPEC F214.11, F214.12, F214.14), computed on the plain lane only. The candidates are the plain-lane engines that beat the baseline;
/// the best by win rate, then more decided pairs, then slug, is the winner. A win is declared only when the winner's reel has a counted wince total of zero:
/// a missing reel or count is "not counted", never a pass.
/// </summary>
internal sealed record Verdict(string Text)
{
    /// <param name="baseline">The baseline engine's slug from its profile; its upper case names the baseline in the text.</param>
    /// <param name="wincesByEngine">The counted wince total of each engine's reel; an engine with no entry is "not counted".</param>
    internal static Verdict Decide(Matrix matrix, string baseline, IReadOnlyDictionary<string, int> wincesByEngine)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(wincesByEngine);
        string name = baseline.ToUpperInvariant();
        Cell? winner = Ranking.Order(matrix, matrix.Engines).FirstOrDefault(cell => MarginRule.Evaluate(cell).Beats);

        if (winner is null)
        {
            return new Verdict($"NOBODY BEATS {name}");
        }

        if (!wincesByEngine.TryGetValue(winner.Engine, out int winces))
        {
            return new Verdict($"BEATS {name} ON PAPER, WINCES: not counted");
        }

        if (winces > 0)
        {
            return new Verdict($"BEATS {name} ON PAPER, WINCES: {winces.ToString(CultureInfo.InvariantCulture)}");
        }

        return new Verdict($"BEATS {name}: {winner.Engine} ({Rate.Format(winner.Total.Rate)}, n={winner.Total.N.ToString(CultureInfo.InvariantCulture)}, winces=0)");
    }
}
