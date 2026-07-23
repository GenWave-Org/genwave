// STORY-213 — The persona ranks inside the law
//
// BDD specification — xUnit (SPEC F82.1–F82.6). PLAN T63 builds the ranker; T64 wires it into the
// provider chain with the per-pick debug line. The ranker is deterministic and LLM-free —
// distribution facts run it thousands of times in-memory with a seeded RNG, no I/O.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
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
        new(new FakePersonaTasteReader(rules ?? []), randomSource, TimeProvider.System, options ?? new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);

    static MediaReference MakeRef(string id, string? artist, string? genre = "Rock") => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: artist,
        Album: null,
        Genre: genre,
        Year: null);

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
        // and weekday clocks over the same pool. FakeTimeProvider's LocalTimeZone (fixed to UTC,
        // STORY-213/T64) makes station-local day/hour equal the UTC instant we construct — no
        // dependency on whatever timezone the test happens to run under.

        static readonly TasteContext SundayMorning = new(DaysOfWeek: [DayOfWeek.Sunday], StartHour: 6, EndHour: 12);

        static IReadOnlyList<PersonaRankCandidate> ZeppelinPool() =>
        [
            MakeCandidate("zep1", "Led Zeppelin", genre: "Rock", energy: 0.5),
            MakeCandidate("other1", "Other Artist A", genre: "Rock", energy: 0.5),
            MakeCandidate("other2", "Other Artist B", genre: "Rock", energy: 0.5),
            MakeCandidate("other3", "Other Artist C", genre: "Rock", energy: 0.5),
        ];

        static async Task<double> ZeppelinShareAsync(PersonaRanker ranker, IReadOnlyList<PersonaRankCandidate> pool, int iterations)
        {
            var zepCount = 0;
            for (var i = 0; i < iterations; i++)
            {
                var result = await ranker.PickAsync(personaId: 1, energyDisposition: 0.0, new EnergyRange(0.0, 1.0), pool, CancellationToken.None);
                Assert.NotNull(result);
                if (result.Candidate.MediaId == "zep1") zepCount++;
            }

            return (double)zepCount / iterations;
        }

        [Fact]
        public static async Task SundayMorningPicksShiftTowardTheArtist()
        {
            // the Sunday-Zeppelin acceptance (F82.2/F82.3): pick share rises measurably past the
            // one-in-four (25%) uniform baseline once the Sunday-morning rule gates open.
            var rule = new TasteRule(new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null), SundayMorning, Weight: 1.0);
            var pool = ZeppelinPool();

            // 2026-07-19 09:00 UTC is a Sunday, inside the rule's 06:00-12:00 window.
            var sundayClock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 19, 9, 0, 0, TimeSpan.Zero));
            var ranker = new PersonaRanker(new FakePersonaTasteReader([rule]), new SeededRandomSource(seed: 21), sundayClock, new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);

            var share = await ZeppelinShareAsync(ranker, pool, iterations: 600);

            Assert.True(share > 0.40, $"expected a measurable Sunday shift toward the artist, got {share:F3}");
        }

        [Fact]
        public static async Task WeekdayBehaviorIsUnchanged()
        {
            // the SAME rule, present but never gated open on a weekday — picks stay at the uniform
            // one-in-four baseline (F82.1's day gate holds).
            var rule = new TasteRule(new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null), SundayMorning, Weight: 1.0);
            var pool = ZeppelinPool();

            // 2026-07-22 09:00 UTC is a Wednesday — outside the rule's day gate entirely.
            var weekdayClock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero));
            var ranker = new PersonaRanker(new FakePersonaTasteReader([rule]), new SeededRandomSource(seed: 21), weekdayClock, new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);

            var share = await ZeppelinShareAsync(ranker, pool, iterations: 600);

            Assert.InRange(share, 0.15, 0.35);
        }
    }

    public static class ScenarioPerPickDebugLine
    {
        // Arrange (T64): a completed pick through the wired provider chain; capture the log.

        [Fact]
        public static async Task OneLineCarriesAllSixAnswerFields()
        {
            // envelope id, pool size, top-3 scores, fired rules, exploration flag, degradation step (F82.6)
            var rule = new TasteRule(new TastePredicate(Artist: "Boards of Canada", Genre: null, Tag: null), AnyTime, Weight: 0.9);
            var pool = new[]
            {
                new EnvelopeCandidateRow(MakeRef("bc1", "Boards of Canada"), Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false),
                new EnvelopeCandidateRow(MakeRef("other1", "Other Artist"), Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false),
            };
            var catalog = new FakePersonaPoolCatalog(pool);

            var persona = new Persona(7, "DJ Test", "", "", "", DateTime.UnixEpoch, DateTime.UnixEpoch);
            var card = new PersonaCard(1, "DJ Test", "", "", [], new VoiceSpec("kokoro", "", 1.0, "en"), EnergyDisposition: 0.0, [], []);
            var personaAccessor = new FakeActivePersonaAccessor { Persona = persona, Card = card };

            // exploration roll (0.99, above the 5% floor) ⇒ not exploration; sample roll (0.0) ⇒ picks
            // the highest-scored candidate — the fired rule makes that "bc1".
            var ranker = new PersonaRanker(new FakePersonaTasteReader([rule]), new StubRandomSource(0.99, 0.0), TimeProvider.System, new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);
            var provider = new RankerPersonaPickProvider(catalog, personaAccessor, ranker, new PersonaRankerOptions());

            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
            var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
            var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
            {
                LeadInBeforeEachTrack = false,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 0,
            });
            var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings { ArtistSeparation = 0 });
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = new Orchestrator(
                identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog,
                new FakeTtsSegmentSource(), personaAccessor, logger,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
                new SpeechDeferralQueue(TimeProvider.System),
                TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero),
                new FakeEnvelopeProvider(SegmentEnvelope.StationDefault),
                provider);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("bc1", item.MediaId);

            var debugLine = Assert.Single(logger.Entries, e => e.Level == LogLevel.Debug);
            Assert.Contains("envelope=station-default", debugLine.Message);
            Assert.Contains("pool=2", debugLine.Message);
            Assert.Contains("top3=[0.900", debugLine.Message);
            Assert.Contains("Boards of Canada", debugLine.Message);
            Assert.Contains("exploration=False", debugLine.Message);
            Assert.Contains("degradation=none", debugLine.Message);
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

    public static class ScenarioGarbageRulesNeverSilentlyDisable
    {
        // Arrange (gh-#87): a persona_taste context of `{}` deserializes TasteContext.DaysOfWeek to
        // null (STJ missing-property default) — pre-fix, TasteMatcher.MatchesDay NRE'd on every pick
        // evaluation and the swallow upstream degraded the whole persona layer to envelope-only,
        // silently. Both legs pinned here: null-tolerant matching, and a WARN + skip (never a fault)
        // for any rule whose evaluation still throws.

        /// <summary>Captures WARN records so the gh-#87 "never silently disabled" contract is assertable.</summary>
        sealed class CapturingLogger : ILogger<PersonaRanker>
        {
            public List<string> Warnings { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    Warnings.Add(formatter(state, exception));
            }
        }

        [Fact]
        public static void NullDaysOfWeekMeansNoDayGate()
        {
            // The exact production shape: `{}` deserialized through STJ leaves DaysOfWeek null
            // despite the record's non-nullable annotation.
            var offSchemaContext = JsonSerializer.Deserialize<TasteContext>("{}")
                ?? throw new InvalidOperationException("'{}' deserialized to null");
            Assert.Null(offSchemaContext.DaysOfWeek);

            var rule = new TasteRule(new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null), offSchemaContext, Weight: 1.0);
            var candidate = MakeCandidate("m1", "Led Zeppelin", genre: null, energy: 0.5);

            // Same semantics as [] — fires every day, never throws.
            Assert.True(TasteMatcher.Matches(rule, candidate, DayOfWeek.Sunday, hour: 8));
            Assert.True(TasteMatcher.Matches(rule, candidate, DayOfWeek.Monday, hour: 8));
        }

        [Fact]
        public static async Task AThrowingRuleWarnsOnceAndThePickContinues()
        {
            // A candidate with a null Moods list (garbage no production seam produces, simulating any
            // future off-schema shape) makes MatchesTag throw for every rule that carries a Tag.
            var throwingRule = new TasteRule(new TastePredicate(Artist: null, Genre: null, Tag: "mellow"), AnyTime, Weight: 1.0);
            var healthyRule = new TasteRule(new TastePredicate(Artist: "Other Artist", Genre: null, Tag: null), AnyTime, Weight: 1.0);
            var poisoned = new PersonaRankCandidate(MediaId: "poisoned", Artist: "Someone", Genre: null, Moods: null!, Energy: 0.5, RotationScore: 0.0);
            var favored = new PersonaRankCandidate(MediaId: "favored", Artist: "Other Artist", Genre: null, Moods: null!, Energy: 0.5, RotationScore: 0.0);

            var logger = new CapturingLogger();
            var ranker = new PersonaRanker(
                new FakePersonaTasteReader([throwingRule, healthyRule]), new StubRandomSource(0.99, 0.0),
                TimeProvider.System, new PersonaRankerOptions(), logger);
            var range = new EnergyRange(0.0, 1.0);

            var result = await ranker.PickAsync(1, 0.0, range, [poisoned, favored], CancellationToken.None);

            // The pick survives, the healthy rule still fires, and the faulted rule WARNs exactly
            // once for the whole pick — not once per candidate.
            Assert.NotNull(result);
            Assert.Equal("favored", result.Candidate.MediaId);
            Assert.Contains(healthyRule, result.FiredRules);
            Assert.Single(logger.Warnings);
            Assert.Contains("gh-#87", logger.Warnings[0]);
        }
    }
}
