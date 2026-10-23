using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Reeling;

/// <summary>
/// Chooses the engines whose clips become wince reels (SPEC F214.13): the tier winner, the runner-up, and the baseline. With no runner-up the set is two.
/// Only engines that passed BOTH gates in this run are considered.
/// </summary>
internal static class WinceSet
{
    /// <exception cref="InvalidDataException">The baseline did not pass both gates, or no challenger did.</exception>
    internal static WinceChoice Choose(EngineProfileSet profiles, IReadOnlyList<GateEntry> gates, Matrix matrix)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(matrix);

        if (!PassedBoth(gates, profiles.Baseline.Slug))
        {
            throw new InvalidDataException("The baseline engine did not pass both gates in this run, so there is nothing to compare the reels with.");
        }

        string[] challengers = [.. profiles.Profiles.Where(p => !p.Baseline && PassedBoth(gates, p.Slug)).Select(p => p.Slug)];
        if (challengers.Length == 0)
        {
            throw new InvalidDataException("No challenger passed both gates in this run, so there is no wince reel to build.");
        }

        IReadOnlyList<Cell> ranked = Ranking.Order(matrix, challengers);
        string winner = ranked[0].Engine;
        string? second = ranked.Count > 1 ? ranked[1].Engine : null;
        return second is null
            ? new WinceChoice([winner, profiles.Baseline.Slug], IsShort: true)
            : new WinceChoice([winner, second, profiles.Baseline.Slug], IsShort: false);
    }

    private static bool PassedBoth(IReadOnlyList<GateEntry> gates, string slug) =>
        gates.Any(g => string.Equals(g.Slug, slug, StringComparison.Ordinal) && g.Container?.Passed == true && g.VoiceSelect?.Passed == true);
}
