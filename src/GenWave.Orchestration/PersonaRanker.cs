using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.1-F82.5 — the deterministic, LLM-free persona ranker (STORY-213, PLAN T63). Scores an
/// envelope-filtered candidate pool against a persona's taste rules and a disposition-positioned
/// energy target, then softmax-samples the Top-K. This is the ranker only: PLAN T64 wires it into
/// <c>Orchestrator</c>'s <see cref="IPersonaPickProvider"/> seam and adds the per-pick debug log
/// (SPEC F82.6) — nothing here touches the Orchestrator. The ONE thing this type logs is a
/// taste rule whose evaluation threw (gh-#87): a silently disabled rule contradicts the F82.6
/// "why did it play that?" observability contract, so it gets a WARN (once per rule per pick)
/// and the pick continues without that rule instead of faulting the whole persona layer.
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
/// deterministically. The station-local day/hour a <see cref="TasteContext"/> gates against
/// (SPEC F82.1) resolves through the live <see cref="IStationClockProvider"/> seam
/// (<c>Station:Timezone</c>, gh-#224) when the composition supplies one — the same optional-seam
/// posture <c>Orchestrator</c>/<c>LlmCopyWriter</c> adopted for gh-#117, so every pre-seam rig
/// keeps compiling — otherwise <see cref="timeProvider"/>'s own
/// <c>TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone)</c> idiom
/// (the container's clock, pre-gh-#224 behavior unchanged).
/// </para>
/// </summary>
public sealed class PersonaRanker(
    IPersonaTasteReader tasteReader,
    IRandomSource randomSource,
    TimeProvider timeProvider,
    PersonaRankerOptions options,
    ILogger<PersonaRanker> logger,
    IStationClockProvider? stationClock = null)
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
    /// computed bias — so its <see cref="PickResult.FiredRules"/> is always empty. The rotation nudge
    /// (SPEC F151.1, STORY-371, PLAN T370) is bias too: <see cref="Score"/> zeroes it for an
    /// exploration pick explicitly (HIGH-1, T370 review) — the nudge has no upstream "empty rules"
    /// equivalent to ride, since it lives on the candidate itself, not on a fetched rule list.
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

        var faultedRules = new HashSet<TasteRule>();
        var topK = candidates
            .Select(candidate => Score(candidate, rules, faultedRules, day, hour, target, isExploration))
            .OrderByDescending(entry => entry.Score)
            .Take(Math.Max(1, options.TopK))
            .ToList();

        var (chosen, _, firedRules) = Sample(topK);
        var topScores = topK.Select(entry => entry.Score).ToList();
        // SPEC F151.4 (STORY-371, PLAN T370) — the SAME Top-K ordering topScores already reports,
        // narrowed to each entry's own candidate.Nudge rather than its score: RankerPersonaPickProvider
        // slices both to the first three for the F82.6 debug line's "nudges top-3" field, index-aligned
        // with top3's own scores.
        var topNudges = topK.Select(entry => entry.Candidate.Nudge).ToList();
        return new PickResult(chosen, isExploration, firedRules, topScores, topNudges);
    }

    double EffectiveExplorationRate => Math.Max(options.ExplorationRate, MinimumExplorationRate);

    (DayOfWeek Day, int Hour) StationLocalNow()
    {
        var now = stationClock?.LocalNow
            ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone);
        return (now.DayOfWeek, now.Hour);
    }

    /// <summary>
    /// SPEC F82.2 — <c>rotationScore + Σ matched-taste·biasGain − |energy − target|·energyPull +
    /// nudge·nudgeGain</c> (the last term SPEC F151.1, STORY-371, PLAN T370). A negative-weight rule
    /// still adds to the sum (it is just negative — dislikes rank down, they are never filtered from
    /// the pool here or anywhere upstream of it, SPEC F82.1). A matched dislike also rides
    /// <c>FiredRules</c> DELIBERATELY (gh-#291): the booth-log pick stamp persists each fired rule's
    /// signed weight (SPEC F86.1) and the admin UI renders that sign — honest diagnostics stay
    /// complete here; the prompt seam (<c>GenWave.Tts.LlmPromptBuilder.DescribeFiredRules</c>) is
    /// what keeps dislikes out of the DJ's spoken taste color.
    ///
    /// A rule whose evaluation throws (gh-#87 — e.g. an off-schema context that survived every write
    /// seam) is WARNed once per pick via <paramref name="faultedRules"/> and skipped for every
    /// candidate — one bad rule never faults the whole persona layer down to envelope-only, and it
    /// never disables silently either.
    ///
    /// <para>
    /// HIGH-1 (T370 review) — <paramref name="isExploration"/> zeroes the nudge term too, the SAME
    /// way <paramref name="rules"/> arrives empty for an exploration pick: the nudge IS bias (a
    /// rotation-history preference), and SPEC F82.4's exploration slice is bias-blind BY LAW, not
    /// taste-blind specifically. Without this, ARCHITECTURE's "the 5% floor is a structural
    /// anti-paving guarantee" promise would be false — a station could nudge one track to +1 and
    /// every other to -1 and still see it favored on every "exploration" pick, which is exactly the
    /// paving-over the floor exists to prevent.
    /// </para>
    /// </summary>
    (PersonaRankCandidate Candidate, double Score, IReadOnlyList<TasteRule> FiredRules) Score(
        PersonaRankCandidate candidate, IReadOnlyList<TasteRule> rules, HashSet<TasteRule> faultedRules,
        DayOfWeek day, int hour, double target, bool isExploration)
    {
        var fired = new List<TasteRule>();
        foreach (var rule in rules)
        {
            if (faultedRules.Contains(rule))
                continue;

            try
            {
                if (TasteMatcher.Matches(rule, candidate, day, hour))
                    fired.Add(rule);
            }
            catch (Exception ex)
            {
                faultedRules.Add(rule);
                logger.LogWarning(
                    ex,
                    "Taste rule {Rule} threw during evaluation — skipping it for this pick (gh-#87; a " +
                    "silently disabled rule contradicts SPEC F82.6).",
                    rule.Predicate.LabelOr("any"));
            }
        }

        var tasteBias = fired.Sum(rule => rule.Weight) * options.BiasGain;
        var energyPenalty = Math.Abs(candidate.Energy - target) * options.EnergyPull;
        // SPEC F151.1/F82.4 (STORY-371, PLAN T370; HIGH-1 T370 review) — the additive rotation-nudge
        // term is zeroed for an exploration pick, the SAME way tasteBias is zeroed above (rules
        // arrives empty): the nudge is bias, and F82.4's exploration slice is bias-blind by law, not
        // taste-blind specifically — this is what makes the 5% floor a genuine anti-paving
        // guarantee rather than a rate that still tracks the nudge underneath. Rung 0 only by
        // construction (F151.2) either way: this method — and candidate.Nudge itself — is never
        // reached by the envelope-only ladder, which scores nothing at all.
        var rotationNudge = isExploration ? 0.0 : candidate.Nudge * options.NudgeGain;
        return (candidate, candidate.RotationScore + tasteBias - energyPenalty + rotationNudge, fired);
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
