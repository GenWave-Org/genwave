using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.1-F82.5 — the deterministic, LLM-free persona ranker (STORY-213, PLAN T63). Scores an
/// envelope-filtered candidate pool against a persona's taste rules and a disposition-positioned
/// energy target, then softmax-samples the Top-K. This is the ranker only: PLAN T64 wires it into
/// <c>Orchestrator</c>'s <see cref="IPersonaPickProvider"/> seam and adds the per-pick debug log
/// (SPEC F82.6) — nothing here touches the Orchestrator, and nothing here logs.
///
/// <para>
/// Depends on <see cref="IPersonaTasteReader"/> — never the write-capable
/// <see cref="Core.Abstractions.IPersonaTasteStore"/> — so F84.2's "no code path that writes
/// persona_taste" guarantee is structural for this type: the write methods simply are not on the
/// seam it holds.
/// </para>
///
/// <para>
/// <see cref="randomSource"/> makes every draw this ranker takes (the exploration roll and the
/// softmax sample) seedable, so distribution facts can run thousands of in-memory picks
/// deterministically. <see cref="timeProvider"/> resolves the station-local day/hour a
/// <see cref="TasteContext"/> gates against (SPEC F82.1) — the same
/// <c>TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone)</c> idiom
/// <c>GenWave.Tts.LlmPromptBuilder.BuildStationClockLine</c> already uses for "station-local now".
/// </para>
/// </summary>
public sealed class PersonaRanker(
    IPersonaTasteReader tasteReader,
    IRandomSource randomSource,
    TimeProvider timeProvider,
    PersonaRankerOptions options)
{
    /// <summary>
    /// SPEC F82.4 — the hard exploration floor: an operator setting of 0 (or anything below this)
    /// still yields this effective rate. Enforced here, in code, never inside
    /// <see cref="PersonaRankerOptions"/> itself.
    /// </summary>
    public const double MinimumExplorationRate = 0.05;

    /// <summary>
    /// Ranks <paramref name="candidates"/> and returns the pick, or <see langword="null"/> when the
    /// pool is empty (the caller's "no persona opinion" case). An exploration pick (SPEC F82.4) never
    /// reads persona taste at all — bias-blind by construction, not by a post-hoc zeroing of a
    /// computed bias — so its <see cref="PickResult.FiredRules"/> is always empty.
    /// </summary>
    public async Task<PickResult?> PickAsync(
        long personaId,
        double energyDisposition,
        EnergyRange range,
        IReadOnlyList<PersonaRankCandidate> candidates,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
            return null;

        var isExploration = randomSource.NextDouble() < EffectiveExplorationRate;
        var rules = isExploration
            ? new List<TasteRule>()
            : (await tasteReader.ListAsync(personaId, source: null, ct)).Select(entry => entry.Rule).ToList();

        var (day, hour) = StationLocalNow();
        var target = EnergyTarget.Compute(range, energyDisposition);

        var topK = candidates
            .Select(candidate => Score(candidate, rules, day, hour, target))
            .OrderByDescending(entry => entry.Score)
            .Take(Math.Max(1, options.TopK))
            .ToList();

        var (chosen, _, firedRules) = Sample(topK);
        var topScores = topK.Select(entry => entry.Score).ToList();
        return new PickResult(chosen, isExploration, firedRules, topScores);
    }

    double EffectiveExplorationRate => Math.Max(options.ExplorationRate, MinimumExplorationRate);

    (DayOfWeek Day, int Hour) StationLocalNow()
    {
        var now = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone);
        return (now.DayOfWeek, now.Hour);
    }

    /// <summary>
    /// SPEC F82.2 — <c>rotationScore + Σ matched-taste·biasGain − |energy − target|·energyPull</c>.
    /// A negative-weight rule still adds to the sum (it is just negative — dislikes rank down, they
    /// are never filtered from the pool here or anywhere upstream of it, SPEC F82.1).
    /// </summary>
    (PersonaRankCandidate Candidate, double Score, IReadOnlyList<TasteRule> FiredRules) Score(
        PersonaRankCandidate candidate, IReadOnlyList<TasteRule> rules, DayOfWeek day, int hour, double target)
    {
        var fired = rules.Where(rule => TasteMatcher.Matches(rule, candidate, day, hour)).ToList();
        var tasteBias = fired.Sum(rule => rule.Weight) * options.BiasGain;
        var energyPenalty = Math.Abs(candidate.Energy - target) * options.EnergyPull;
        return (candidate, candidate.RotationScore + tasteBias - energyPenalty, fired);
    }

    /// <summary>
    /// SPEC F82.3 — softmax over <paramref name="topK"/>'s scores (temperature-scaled, max-shifted for
    /// numeric stability), sampled with one <see cref="randomSource"/> draw. Every candidate keeps a
    /// nonzero selection probability regardless of how negative its score is — softmax never assigns
    /// exactly zero — so a heavily disliked candidate can still be picked, just less often.
    /// </summary>
    (PersonaRankCandidate Candidate, double Score, IReadOnlyList<TasteRule> FiredRules) Sample(
        IReadOnlyList<(PersonaRankCandidate Candidate, double Score, IReadOnlyList<TasteRule> FiredRules)> topK)
    {
        if (topK.Count == 1)
            return topK[0];

        var maxScore = topK.Max(entry => entry.Score);
        var weights = topK.Select(entry => Math.Exp((entry.Score - maxScore) / options.Temperature)).ToList();
        var roll = randomSource.NextDouble() * weights.Sum();

        var cumulative = 0.0;
        for (var i = 0; i < topK.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return topK[i];
        }

        return topK[^1];
    }
}
