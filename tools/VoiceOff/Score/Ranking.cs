using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Score;

/// <summary>
/// The one ordering of engines on the plain lane (SPEC F214.11, F214.13): engines that beat the margin first, then win rate, then more decided
/// pairs, then slug. The verdict's winner and the wince reel's winner and runner-up both come from here, so they cannot drift apart.
/// An engine with no plain pairs still ranks, after every engine that has a rate.
/// </summary>
internal static class Ranking
{
    /// <summary>The plain-lane cell of each named engine, best first.</summary>
    internal static IReadOnlyList<Cell> Order(Matrix matrix, IEnumerable<string> engines)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(engines);
        return [.. engines
            .Select(engine => matrix.Find(engine, RenderLane.Plain))
            .OrderByDescending(cell => MarginRule.Evaluate(cell).Beats)
            .ThenByDescending(cell => cell.Total.Rate)
            .ThenByDescending(cell => cell.Total.N)
            .ThenBy(cell => cell.Engine, StringComparer.Ordinal)];
    }
}
