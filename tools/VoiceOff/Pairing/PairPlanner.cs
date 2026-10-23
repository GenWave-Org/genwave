using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Pairing;

/// <summary>
/// SPEC F214.9: for each challenger, each lane it was rendered in, and each gauntlet line, one pair against the baseline's plain clip
/// ("as shipped", in both lanes). The coin flip that puts the baseline on side A or B is <c>rng.Next(2)</c>. Pure: the clip check is injected.
/// </summary>
internal static class PairPlanner
{
    /// <param name="usable">True when the clip for (engine, lane, line id) rendered ok and can be read.</param>
    internal static PairPlan Plan(
        string baseline, IEnumerable<EngineProfile> challengers, IReadOnlyList<GauntletLine> lines, Func<string, RenderLane, string, bool> usable, Random rng)
    {
        ArgumentNullException.ThrowIfNull(challengers);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(usable);
        ArgumentNullException.ThrowIfNull(rng);

        List<PlannedPair> pairs = [];
        int skipped = 0;
        foreach (EngineProfile challenger in challengers.OrderBy(c => c.Slug, StringComparer.Ordinal))
        {
            foreach (RenderLane lane in LanePlan.For(challenger))
            {
                foreach (GauntletLine line in lines)
                {
                    if (usable(challenger.Slug, lane, line.Id) && usable(baseline, RenderLane.Plain, line.Id))
                    {
                        pairs.Add(new PlannedPair(challenger.Slug, lane, line, BaselineIsA: rng.Next(2) == 0));
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }
        }

        return new PairPlan(pairs, skipped);
    }
}
