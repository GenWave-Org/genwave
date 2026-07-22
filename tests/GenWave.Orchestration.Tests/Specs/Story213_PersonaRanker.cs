// STORY-213 — The persona ranks inside the law
//
// BDD specification — xUnit (SPEC F82.1–F82.6). PLAN T63 builds the ranker; T64 wires it into the
// provider chain with the per-pick debug line. The ranker is deterministic and LLM-free —
// distribution facts run it thousands of times in-memory with a seeded RNG, no I/O.

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeaturePersonaRanker
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static readonly TasteContext AnyTime = new(DaysOfWeek: [], StartHour: null, EndHour: null);

    static PersonaRankCandidate MakeCandidate(
        string id, string? artist, string? genre, double energy, double rotationScore = 0.0, IReadOnlyList<string>? moods = null) =>
        new(MediaId: id, Artist: artist, Genre: genre, Moods: moods ?? [], Energy: energy, RotationScore: rotationScore);

    static PersonaRanker BuildRanker(IRandomSource randomSource, IReadOnlyList<TasteRule>? rules = null, PersonaRankerOptions? options = null) =>
        new(new FakePersonaTasteReader(rules ?? []), randomSource, TimeProvider.System, options ?? new PersonaRankerOptions());

    public static class ScenarioPredicateAndContextMatching
    {
        // Arrange (T63): taste rules with artist/genre/tag predicates and day-of-week/hour
        // contexts; candidates crafted to hit each matching edge.

        [Fact]
        public static void PredicateFieldsAndMatch()
        {
            // artist+genre both present ⇒ both must match (F82.1 AND semantics)
            var rule = new TasteRule(new TastePredicate(Artist: "Boards of Canada", Genre: "IDM", Tag: null), AnyTime, Weight: 0.5);
            var matchesBoth = MakeCandidate("m1", "Boards of Canada", "IDM", energy: 0.5);
            var matchesArtistOnly = MakeCandidate("m2", "Boards of Canada", "Ambient", energy: 0.5);

            Assert.True(TasteMatcher.Matches(rule, matchesBoth, DayOfWeek.Wednesday, hour: 12));
            Assert.False(TasteMatcher.Matches(rule, matchesArtistOnly, DayOfWeek.Wednesday, hour: 12));
        }

        [Fact]
        public static void MatchingIsCaseInsensitive()
        {
            var rule = new TasteRule(new TastePredicate(Artist: "led zeppelin", Genre: null, Tag: null), AnyTime, Weight: 1.0);
            var candidate = MakeCandidate("m1", "LED ZEPPELIN", genre: null, energy: 0.5);

            Assert.True(TasteMatcher.Matches(rule, candidate, DayOfWeek.Wednesday, hour: 12));
        }

        [Fact]
        public static void ContextGatesByDayOfWeek()
        {
            // Sunday rule fires Sunday, not Monday (F82.1, F82.5)
            var rule = new TasteRule(
                new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null),
                new TasteContext(DaysOfWeek: [DayOfWeek.Sunday], StartHour: null, EndHour: null),
                Weight: 1.0);
            var candidate = MakeCandidate("m1", "Led Zeppelin", genre: null, energy: 0.5);

            Assert.True(TasteMatcher.Matches(rule, candidate, DayOfWeek.Sunday, hour: 8));
            Assert.False(TasteMatcher.Matches(rule, candidate, DayOfWeek.Monday, hour: 8));
        }

        [Fact]
        public static void ContextGatesByHour()
        {
            var rule = new TasteRule(
                new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null),
                new TasteContext(DaysOfWeek: [], StartHour: 6, EndHour: 12),
                Weight: 1.0);
            var candidate = MakeCandidate("m1", "Led Zeppelin", genre: null, energy: 0.5);

            Assert.True(TasteMatcher.Matches(rule, candidate, DayOfWeek.Wednesday, hour: 8));
            Assert.False(TasteMatcher.Matches(rule, candidate, DayOfWeek.Wednesday, hour: 13));
            Assert.False(TasteMatcher.Matches(rule, candidate, DayOfWeek.Wednesday, hour: 5));
        }
    }

    public static class ScenarioDispositionShapesWithinTheLaw
    {
        // Arrange (T63): two personas, energyDisposition -1 and +1, same envelope, same
        // pool, seeded RNG, N picks each.

        static IReadOnlyList<PersonaRankCandidate> EnergySpreadPool(int steps) =>
            Enumerable.Range(0, steps)
                .Select(i => MakeCandidate($"e{i}", artist: null, genre: null, energy: (double)i / (steps - 1)))
                .ToList();

        static async Task<double> MeanPickedEnergyAsync(
            PersonaRanker ranker, EnergyRange range, double disposition, IReadOnlyList<PersonaRankCandidate> pool, int iterations)
        {
            var total = 0.0;
            for (var i = 0; i < iterations; i++)
            {
                var result = await ranker.PickAsync(personaId: 1, disposition, range, pool, CancellationToken.None);
                Assert.NotNull(result);
                total += result.Candidate.Energy;
            }

            return total / iterations;
        }

        [Fact]
        public static async Task OppositeDispositionsProduceMeasurablyDifferentDistributions()
        {
            // mean picked energy differs beyond noise (F82.2)
            var pool = EnergySpreadPool(steps: 11);
            var range = new EnergyRange(0.0, 1.0);

            var meanHigh = await MeanPickedEnergyAsync(
                BuildRanker(new SeededRandomSource(seed: 1)), range, disposition: 1.0, pool, iterations: 300);
            var meanLow = await MeanPickedEnergyAsync(
                BuildRanker(new SeededRandomSource(seed: 2)), range, disposition: -1.0, pool, iterations: 300);

            Assert.True(meanHigh - meanLow > 0.2, $"expected a measurable gap, got high={meanHigh:F3} low={meanLow:F3}");
        }

        [Fact]
        public static async Task EveryPickStaysInsideTheEnvelopeRange()
        {
            // law holds for both personas (F82.2 — same law, different feel)
            var range = new EnergyRange(0.2, 0.8);
            var pool = Enumerable.Range(0, 13)
                .Select(i => MakeCandidate($"e{i}", artist: null, genre: null, energy: 0.2 + i * 0.05))
                .ToList();
            var ranker = BuildRanker(new SeededRandomSource(seed: 3));

            for (var i = 0; i < 50; i++)
            {
                var favoringHigh = await ranker.PickAsync(1, energyDisposition: 1.0, range, pool, CancellationToken.None);
                var favoringLow = await ranker.PickAsync(1, energyDisposition: -1.0, range, pool, CancellationToken.None);

                Assert.NotNull(favoringHigh);
                Assert.NotNull(favoringLow);
                Assert.InRange(favoringHigh.Candidate.Energy, range.Min, range.Max);
                Assert.InRange(favoringLow.Candidate.Energy, range.Min, range.Max);
            }
        }

        [Fact]
        public static void TargetClampsAtTheRangeEdge()
        {
            // disposition pushing past Min/Max clamps: target = clamp(mid + d·half, min, max) (F82.2)
            var range = new EnergyRange(0.3, 0.7);

            Assert.Equal(0.7, EnergyTarget.Compute(range, disposition: 1.5));
            Assert.Equal(0.3, EnergyTarget.Compute(range, disposition: -1.5));
        }
    }

    public static class ScenarioSignatureRuleShiftsSunday
    {
        // Arrange (T64): one authored Sunday-morning artist rule; simulated Sunday-morning
        // and weekday clocks over the same pool.

        [Fact(Skip = "Pending T64 — see docs/PLAN.md")]
        public static void SundayMorningPicksShiftTowardTheArtist()
        {
            // the Sunday-Zeppelin acceptance (F82.2/F82.3): pick share rises measurably
            Assert.Fail("pending T64");
        }

        [Fact(Skip = "Pending T64 — see docs/PLAN.md")]
        public static void WeekdayBehaviorIsUnchanged()
        {
            Assert.Fail("pending T64");
        }
    }

    public static class ScenarioPerPickDebugLine
    {
        // Arrange (T64): a completed pick through the wired provider chain; capture the log.

        [Fact(Skip = "Pending T64 — see docs/PLAN.md")]
        public static void OneLineCarriesAllSixAnswerFields()
        {
            // envelope id, pool size, top-3 scores, fired rules, exploration flag, degradation step (F82.6)
            Assert.Fail("pending T64");
        }
    }

    public static class SadPathFloorsAndNegativeWeights
    {
        [Fact]
        public static async Task ExplorationFloorOverridesAZeroSetting()
        {
            // operator sets 0 ⇒ observed exploration ≥ 5% over N seeded picks (F82.4)
            var pool = new[]
            {
                MakeCandidate("a", artist: null, genre: null, energy: 0.5),
                MakeCandidate("b", artist: null, genre: null, energy: 0.5),
            };
            var options = new PersonaRankerOptions { ExplorationRate = 0.0 };
            var ranker = BuildRanker(new SeededRandomSource(seed: 42), options: options);
            var range = new EnergyRange(0.0, 1.0);

            const int iterations = 5000;
            var explorationCount = 0;
            for (var i = 0; i < iterations; i++)
            {
                var result = await ranker.PickAsync(1, 0.0, range, pool, CancellationToken.None);
                Assert.NotNull(result);
                if (result.IsExploration)
                    explorationCount++;
            }

            var observedRate = (double)explorationCount / iterations;
            Assert.InRange(observedRate, 0.03, 0.08);
        }

        [Fact]
        public static async Task ExplorationPicksAreBiasBlind()
        {
            // an exploration pick ignores taste terms entirely (F82.4)
            var likedRule = new TasteRule(new TastePredicate(Artist: "Boards of Canada", Genre: null, Tag: null), AnyTime, Weight: 1.0);
            var pool = new[]
            {
                MakeCandidate("liked", artist: "Boards of Canada", genre: null, energy: 0.5),
                MakeCandidate("other", artist: "Other Artist", genre: null, energy: 0.5),
            };
            // exploration roll (0.0, below the 5% floor) forces exploration; the sample roll doesn't
            // matter to this fact.
            var ranker = BuildRanker(new StubRandomSource(0.0, 0.5), [likedRule]);
            var range = new EnergyRange(0.0, 1.0);

            var result = await ranker.PickAsync(1, 0.0, range, pool, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result.IsExploration);
            Assert.Empty(result.FiredRules);
        }

        [Fact]
        public static async Task ANegativeWeightReducesTheScore()
        {
            var dislikeRule = new TasteRule(new TastePredicate(Artist: "Nickelback", Genre: null, Tag: null), AnyTime, Weight: -0.8);
            var liked = MakeCandidate("liked", artist: "Other Artist", genre: null, energy: 0.5, rotationScore: 1.0);
            var disliked = MakeCandidate("disliked", artist: "Nickelback", genre: null, energy: 0.5, rotationScore: 1.0);
            // exploration roll (0.99, above the floor) ⇒ not exploration; sample roll (0.0) ⇒ picks
            // whichever candidate scored highest.
            var ranker = BuildRanker(new StubRandomSource(0.99, 0.0), [dislikeRule]);
            var range = new EnergyRange(0.0, 1.0);

            var result = await ranker.PickAsync(1, 0.0, range, [liked, disliked], CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("liked", result.Candidate.MediaId);
        }

        [Fact]
        public static async Task ANegativeWeightNeverRemovesACandidateFromThePool()
        {
            // dislikes rank down, never filter out — the envelope alone filters (F81.2/F82.1)
            var dislikeRule = new TasteRule(new TastePredicate(Artist: "Nickelback", Genre: null, Tag: null), AnyTime, Weight: -1.0);
            var favored = MakeCandidate("favored", artist: "Other Artist", genre: null, energy: 0.5, rotationScore: 1.0);
            var disliked = MakeCandidate("disliked", artist: "Nickelback", genre: null, energy: 0.5, rotationScore: 1.0);
            // exploration roll (0.99, above the floor) ⇒ not exploration; sample roll (0.999) ⇒ softmax
            // still reaches the heavily disliked candidate — it was never removed from the pool.
            var ranker = BuildRanker(new StubRandomSource(0.99, 0.999), [dislikeRule]);
            var range = new EnergyRange(0.0, 1.0);

            var result = await ranker.PickAsync(1, 0.0, range, [favored, disliked], CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("disliked", result.Candidate.MediaId);
        }
    }
}
