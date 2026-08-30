// STORY-371 — The nudge in the ranker (SPEC F151.1–F151.4 · PLAN T359, T370)
//
// BDD specification — xUnit. AC4 (T359, the pool projection's carrier half) is already green. This
// file adds AC5–AC9 (T370: PersonaRanker.Score's additive term, its rung-0-only reach, the F151.3
// simulation bound, and F82.6/F86 observability). Arrange sketch: pure in-memory arrangement of
// PersonaRankCandidate/PersonaRanker (Story213_PersonaRanker.cs's own idiom — no I/O, a seeded
// IRandomSource) plus, for AC7/AC8, the F84.2/Story213 simulation idiom: N picks (500 per STORY-371
// AC7/AC8) run in-memory against a fixed candidate pool with nudges set per-track, tallying the
// winner-share distribution — the same seeded-RNG/iterate/tally/assert-a-bound shape as
// Story213_PersonaRanker.cs's own exploration-rate simulation.

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureTheNudgeInTheRanker
{
    static MediaReference MakeRef(string id, string? artist = null) => new(
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
        Genre: "Rock",
        Year: null);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the carrier, the term, and its bounds
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCandidateCarriesTheNudge
    {
        static readonly MediaReference Track = new(
            MediaId: "nudge-carrier",
            Locator: "/media/nudge-carrier.mp3",
            Title: "Nudge Carrier",
            Loudness: new Loudness(-23.0, -1.0, true),
            DurationMs: null,
            SampleRate: null,
            Channels: null,
            BitrateKbps: null,
            Artist: "Artist",
            Album: null,
            Genre: "Rock",
            Year: null);

        // Given a pool row with nudge 0.6 and play_count 3 (SPEC F151.1's carrier half, PLAN T359),
        // projected through RankerPersonaPickProvider.ToRankCandidate — the ONE production mapping
        // from EnvelopeCandidateRow onto PersonaRankCandidate.
        readonly PersonaRankCandidate candidate = RankerPersonaPickProvider.ToRankCandidate(
            new EnvelopeCandidateRow(Track, Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false)
            {
                Nudge = 0.6,
                PlayCount = 3,
            });

        [Fact]
        public void ThePersonaRankCandidateHasNudgeZeroPointSix() =>
            Assert.Equal(0.6, candidate.Nudge);

        [Fact]
        public void ThePersonaRankCandidateHasPlayCountFromTheLedger() =>
            Assert.Equal(3, candidate.PlayCount);
    }

    public sealed class ScenarioTheRankerTerm
    {
        // Given two identical candidates except nudge 0.6 vs 0, NudgeGain 0.5, When they are scored
        // (via PickAsync's own TopScores, the ranker's public observation point — Score itself is
        // private).
        [Fact]
        public async Task TheScoresDifferByExactlyZeroPointThree()
        {
            var nudged = new PersonaRankCandidate("nudged", null, null, [], Energy: 0.5, RotationScore: 0.0, Nudge: 0.6);
            var plain = new PersonaRankCandidate("plain", null, null, [], Energy: 0.5, RotationScore: 0.0, Nudge: 0.0);
            var options = new PersonaRankerOptions { NudgeGain = 0.5 };
            // exploration roll (0.99, above the 5% floor) ⇒ not exploration — both candidates are
            // genuinely scored.
            var ranker = new PersonaRanker(
                new FakePersonaTasteReader([]), new StubRandomSource(0.99, 0.0), TimeProvider.System, options,
                NullLogger<PersonaRanker>.Instance);
            var range = new EnergyRange(0.0, 1.0);

            var result = await ranker.PickAsync(1, 0.0, range, [nudged, plain], CancellationToken.None);

            Assert.NotNull(result);
            // Descending order (SPEC F82.3): the nudged candidate's +0.3 term always outscores the
            // plain one — nothing else differs between them.
            Assert.Equal(2, result.TopScores.Count);
            Assert.Equal(0.3, result.TopScores[0] - result.TopScores[1], precision: 9);
        }
    }

    public sealed class ScenarioRungZeroOnly
    {
        // A minimal IMediaCatalog double for the envelope-only ladder (no persona pick provider
        // bound, SPEC F81.2's own NoOpPersonaPickProvider default): GetRotationCandidateAsync
        // round-robins a fixed pool, decorating the returned RotationCandidate.Nudge on request. The
        // envelope-only rungs never read that property at all — MusicSelectionPolicy's own
        // "persona-off" path forwards whatever the catalog returns straight through with no scoring
        // step of any kind — so decorating it here proves the property is genuinely inert on this
        // rung, not merely absent from this fixture.
        sealed class NudgeDecoratingMediaCatalog(IReadOnlyList<MediaReference> pool, bool decorate) : IMediaCatalog
        {
            int nextIndex;

            public Task<RotationCandidate?> GetRotationCandidateAsync(
                LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
            {
                var media = pool[nextIndex % pool.Count];
                nextIndex++;
                var candidate = new RotationCandidate(media, RepeatedRecent: false, RepeatedArtist: false);
                return Task.FromResult<RotationCandidate?>(decorate ? candidate with { Nudge = 0.9 } : candidate);
            }

            public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct) =>
                Task.FromResult(pool.FirstOrDefault(m => m.MediaId == mediaId));

            public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct) =>
                Task.FromResult(pool.FirstOrDefault(m => m.MediaId == mediaId));

            public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
                Task.FromResult(pool.Count == 0 ? null : pool[0]);

            public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
                Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

            public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
                Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

            public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<FacetValue>>([]);
        }

        // Given the persona layer disabled (no IPersonaPickProvider bound — the envelope-only ladder
        // runs, never PersonaRanker.Score), When 1,000 picks run against candidates that DO carry a
        // non-zero Nudge exactly as SPEC F151.1's pool projection would stamp them, Then the picked
        // sequence is byte-identical to the same 1,000 picks against the same underlying pool with
        // every Nudge zeroed. MED-3 (T370 review) caveat, left here deliberately rather than
        // deleted: this fact CANNOT fail by construction — each catalog instance's own nextIndex
        // counter advances in lockstep regardless of anything about Nudge, since
        // IMediaCatalog.GetRotationCandidateAsync returns a single candidate with nothing to rank
        // against. It stays as a cheap byte-identical sanity check; TheRankerIsNeverInvokedWithNoActivePersonaBound
        // below is the fact that actually PROVES rung 0's own scoring code never runs.
        [Fact]
        public static async Task ThePickSequenceIsIdenticalWhetherOrNotCandidatesCarryANudge()
        {
            var pool = new[] { MakeRef("a"), MakeRef("b"), MakeRef("c") };
            var policyWithNudges = new MusicSelectionPolicy(
                new NudgeDecoratingMediaCatalog(pool, decorate: true), NullLogger<MusicSelectionPolicy>.Instance);
            var policyWithoutNudges = new MusicSelectionPolicy(
                new NudgeDecoratingMediaCatalog(pool, decorate: false), NullLogger<MusicSelectionPolicy>.Instance);
            var scope = new LibraryScope([1L]);

            var pickedWithNudges = new List<string>();
            var pickedWithoutNudges = new List<string>();
            for (var i = 0; i < 1000; i++)
            {
                var withNudges = await policyWithNudges.SelectMusicCandidateAsync(scope, [], 0, null, null, CancellationToken.None);
                var withoutNudges = await policyWithoutNudges.SelectMusicCandidateAsync(scope, [], 0, null, null, CancellationToken.None);
                Assert.NotNull(withNudges.Candidate);
                Assert.NotNull(withoutNudges.Candidate);
                pickedWithNudges.Add(withNudges.Candidate.Media.MediaId);
                pickedWithoutNudges.Add(withoutNudges.Candidate.Media.MediaId);
            }

            Assert.Equal(pickedWithoutNudges, pickedWithNudges);
        }

        /// <summary>
        /// MED-3 (T370 review) — a counting <see cref="IRandomSource"/> double, spying on
        /// <see cref="PersonaRanker"/> itself rather than <see cref="IPersonaPickProvider"/>: SPEC
        /// F81.6's own rung-0 contract has <c>MusicSelectionPolicy.TryRungZeroAsync</c> consult
        /// WHATEVER <see cref="IPersonaPickProvider"/> is bound on EVERY pick, unconditionally — a
        /// "no persona opinion" null answer, never a skipped call — so "zero invocations of the
        /// interface method" is not a true statement about this architecture regardless of Nudge.
        /// What SPEC F151.2 actually promises is that <see cref="PersonaRanker.PickAsync"/> — where
        /// the nudge term lives — never runs without an active persona:
        /// <see cref="RankerPersonaPickProvider.TryPickAsync"/> returns null the moment
        /// <c>personaAccessor.ResolveAsync()</c> answers null, BEFORE constructing a single
        /// <see cref="PersonaRankCandidate"/> or calling the ranker. Proven here because
        /// <see cref="PersonaRanker.PickAsync"/>'s own first statement draws from
        /// <see cref="IRandomSource"/> (the exploration roll, SPEC F82.4) — zero draws is only
        /// possible if <c>PickAsync</c> itself was never entered.
        /// </summary>
        sealed class CountingRandomSource : IRandomSource
        {
            public int CallCount { get; private set; }

            public double NextDouble()
            {
                CallCount++;
                return 0.5;
            }
        }

        [Fact]
        public static async Task TheRankerIsNeverInvokedWithNoActivePersonaBound()
        {
            var pool = new[]
            {
                new EnvelopeCandidateRow(MakeRef("a"), Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false)
                {
                    Nudge = 0.9,
                },
            };
            var catalog = new FakePersonaPoolCatalog(pool);
            var randomSource = new CountingRandomSource();
            var ranker = new PersonaRanker(
                new FakePersonaTasteReader([]), randomSource, TimeProvider.System, new PersonaRankerOptions(),
                NullLogger<PersonaRanker>.Instance);
            // FakeActivePersonaAccessor.Persona defaults to null — "the persona layer disabled."
            var personaAccessor = new FakeActivePersonaAccessor();
            var provider = new RankerPersonaPickProvider(catalog, personaAccessor, ranker, new PersonaRankerOptions());
            var scope = new LibraryScope([1L]);

            for (var i = 0; i < 1000; i++)
            {
                var result = await provider.TryPickAsync(scope, [], 0, SegmentEnvelope.StationDefault, CancellationToken.None);
                Assert.Null(result);
            }

            Assert.Equal(0, randomSource.CallCount);
        }
    }

    public sealed class ScenarioTheBoundSimulated
    {
        // Given every track but one at nudge -1 and that one at +1 (18 tracks total — SPEC
        // PersonaRankerOptions.TopK's own default, so every candidate enters the softmax).
        //
        // DERIVATION (T370 review HIGH-1 restatement, harness's ACTUAL inputs):
        //   gap            = NudgeGain × (favoured − other) = 0.5 × (1.0 − (−1.0)) = 0.5 × 2 = 1.0
        //   softmax ratio  = e^(gap / Temperature) = e^(1.0 / 0.7) = 4.1727
        //   non-explore share (N=18) = 4.1727 / (4.1727 + 17) = 4.1727 / 21.1727 = 19.71 %
        //   ε (harness's exploration rate) = ExplorationRate 0.0 below pins PersonaRanker's own hard
        //     floor (MinimumExplorationRate, SPEC F82.4) as the EFFECTIVE rate — ε = 0.05, not the
        //     nominal 0 setting (F82.4's own "an operator setting of 0 still yields this effective
        //     rate" law). HIGH-1 (T370 review): an exploration pick is now nudge-blind too (bias-blind
        //     BY LAW, not taste-blind specifically), so it contributes the UNIFORM 1/18 baseline, not
        //     a nudge-weighted share.
        //   blended cap    = (1 − ε)·19.71 % + ε·(1 / 18) = 0.95×0.1971 + 0.05×0.05556
        //                   = 0.18725 + 0.00278 = 0.19003 ≈ 19.00 %
        //   σ (N=500 picks, p≈0.19) = sqrt(p·(1−p) / 500) = sqrt(0.19×0.81 / 500) ≈ 0.0175 ≈ 1.75 pts
        //   3σ bound       = 19.00 % + 3×1.75 % = 19.00 % + 5.26 % = 24.27 %
        // MED-1 (T370 review): the seed is fixed — no flake to buy slack for — so the assertion below
        // is exactly derivation + 3σ (24.27 %, rounded up to 24.3 % for float safety), a genuine
        // regression pin, not a generous "won't ever trip" ceiling.
        const int TrackCount = 18;
        const string FavouredId = "favoured";
        const int Iterations = 500;
        // Derived cap (19.00%) + 3σ (5.26 pts) = 24.27%, rounded UP to 24.3% for float safety —
        // see the DERIVATION block above (MED-1, T370 review: a regression pin, not slack).
        const double ShareBound = 0.243;

        static IReadOnlyList<PersonaRankCandidate> MakePool()
        {
            var pool = new List<PersonaRankCandidate>
            {
                new(FavouredId, null, null, [], Energy: 0.5, RotationScore: 0.0, Nudge: 1.0),
            };
            for (var i = 0; i < TrackCount - 1; i++)
                pool.Add(new PersonaRankCandidate($"other{i}", null, null, [], Energy: 0.5, RotationScore: 0.0, Nudge: -1.0));
            return pool;
        }

        static PersonaRanker BuildRanker(int seed) => new(
            new FakePersonaTasteReader([]), new SeededRandomSource(seed), TimeProvider.System,
            new PersonaRankerOptions { ExplorationRate = 0.0 }, NullLogger<PersonaRanker>.Instance);

        [Fact]
        public static async Task TheFavouredTracksShareStaysAtOrBelowTheExplorationAdjustedCap()
        {
            var pool = MakePool();
            var ranker = BuildRanker(seed: 371);
            var range = new EnergyRange(0.0, 1.0);

            var favouredCount = 0;
            for (var i = 0; i < Iterations; i++)
            {
                var result = await ranker.PickAsync(1, 0.0, range, pool, CancellationToken.None);
                Assert.NotNull(result);
                if (result.Candidate.MediaId == FavouredId) favouredCount++;
            }

            var share = (double)favouredCount / Iterations;
            // Derived cap 19.00% + 3σ (5.26 pts) = 24.27%, rounded up to 24.3% (ShareBound) — a
            // regression pin over generous slack (MED-1, T370 review): this seed observes ~21.8%.
            Assert.True(
                share <= ShareBound,
                $"expected the favoured share at or below the derived cap + 3σ ({ShareBound:P1}), got {share:P1}");
        }

        [Fact]
        public static async Task ExplorationPicksAreAtLeastFivePercent()
        {
            var pool = MakePool();
            var ranker = BuildRanker(seed: 372);
            var range = new EnergyRange(0.0, 1.0);

            var explorationCount = 0;
            for (var i = 0; i < Iterations; i++)
            {
                var result = await ranker.PickAsync(1, 0.0, range, pool, CancellationToken.None);
                Assert.NotNull(result);
                if (result.IsExploration) explorationCount++;
            }

            // MED-2 (T370 review) — two distinct assertions: a TIGHT band around this fixed seed's
            // own deterministic value (37/500 = 7.4% — no statistical slack needed, the seed never
            // varies), AND the F82.4 LAW itself (PersonaRanker.MinimumExplorationRate, not a
            // hardcoded 0.05 literal) — the floor is 5% by law regardless of which seed a future
            // edit to this fact might pick.
            var rate = (double)explorationCount / Iterations;
            Assert.InRange(rate, 0.06, 0.09);
            Assert.True(
                rate >= PersonaRanker.MinimumExplorationRate,
                $"F82.4's 5% floor is a hard law — got {rate:P1} ({explorationCount}/{Iterations})");
        }
    }

    public sealed class ScenarioAUniformNudgeChangesNothing
    {
        // Given every track at +1, When 500 picks run, Then the distribution matches every track at
        // 0 — proven deterministically (not statistically): a uniform nudge shifts EVERY candidate's
        // score by the SAME constant, which the softmax's own max-shift step (SPEC F82.3) cancels out
        // identically, so two rankers fed identically-seeded RNG streams over the two pools must pick
        // the exact same sequence, pick for pick.
        static IReadOnlyList<PersonaRankCandidate> MakePool(double nudge) =>
            Enumerable.Range(0, 18)
                .Select(i => new PersonaRankCandidate($"t{i}", null, null, [], Energy: 0.5, RotationScore: 0.0, Nudge: nudge))
                .ToList();

        [Fact]
        public static async Task TheDistributionMatchesEveryTrackAtZero()
        {
            var poolAllPositive = MakePool(nudge: 1.0);
            var poolAllZero = MakePool(nudge: 0.0);
            var range = new EnergyRange(0.0, 1.0);

            var rankerPositive = new PersonaRanker(
                new FakePersonaTasteReader([]), new SeededRandomSource(seed: 88), TimeProvider.System,
                new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);
            var rankerZero = new PersonaRanker(
                new FakePersonaTasteReader([]), new SeededRandomSource(seed: 88), TimeProvider.System,
                new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);

            var pickedPositive = new List<string>();
            var pickedZero = new List<string>();
            for (var i = 0; i < 500; i++)
            {
                var resultPositive = await rankerPositive.PickAsync(1, 0.0, range, poolAllPositive, CancellationToken.None);
                var resultZero = await rankerZero.PickAsync(1, 0.0, range, poolAllZero, CancellationToken.None);
                Assert.NotNull(resultPositive);
                Assert.NotNull(resultZero);
                pickedPositive.Add(resultPositive.Candidate.MediaId);
                pickedZero.Add(resultZero.Candidate.MediaId);
            }

            Assert.Equal(pickedZero, pickedPositive);
        }
    }

    public sealed class ScenarioObservability
    {
        // Given a pick whose winner had nudge 0.6, When the per-pick log line and the booth-log
        // chips are read — the same wired-provider harness Story213_PersonaRanker.cs's own
        // ScenarioPerPickDebugLine uses (real RankerPersonaPickProvider + MusicSelectionPolicy +
        // Orchestrator chain, a CapturingLogger<MusicSelectionPolicy> for the F82.6 line).
        static async Task<(MediaItem? Item, CapturingLogger<MusicSelectionPolicy> Logger)> RunWiredPickAsync()
        {
            var rule = new TasteRule(
                new TastePredicate(Artist: "Boards of Canada", Genre: null, Tag: null),
                new TasteContext(DaysOfWeek: [], StartHour: null, EndHour: null), Weight: 0.9);
            var pool = new[]
            {
                new EnvelopeCandidateRow(MakeRef("bc1", "Boards of Canada"), Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false)
                {
                    Nudge = 0.6,
                },
                new EnvelopeCandidateRow(MakeRef("other1", "Other Artist"), Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false)
                {
                    Nudge = -0.2,
                },
            };
            var catalog = new FakePersonaPoolCatalog(pool);

            var persona = new Persona(7, "DJ Test", "", "", "", DateTime.UnixEpoch, DateTime.UnixEpoch);
            var card = new PersonaCard(1, "DJ Test", "", "", [], new VoiceSpec("kokoro", "", 1.0, "en"), EnergyDisposition: 0.0, [], []);
            var personaAccessor = new FakeActivePersonaAccessor { Persona = persona, Card = card };

            // exploration roll (0.99, above the 5% floor) ⇒ not exploration; sample roll (0.0) ⇒
            // picks the highest-scored candidate — the fired rule plus nudge makes that "bc1".
            var ranker = new PersonaRanker(
                new FakePersonaTasteReader([rule]), new StubRandomSource(0.99, 0.0), TimeProvider.System,
                new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);
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
            var logger = new CapturingLogger<MusicSelectionPolicy>();
            var musicSelectionPolicy = new MusicSelectionPolicy(
                catalog, logger, new FakeEnvelopeProvider(SegmentEnvelope.StationDefault), provider);
            var orchestrator = new Orchestrator(
                identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy,
                new FakeTtsSegmentSource(), personaAccessor, NullLogger<Orchestrator>.Instance,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
                new SpeechDeferralQueue(TimeProvider.System),
                TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            return (item, logger);
        }

        [Fact]
        public static async Task TheLogLineCarriesTheTopThreeNudges()
        {
            var (item, logger) = await RunWiredPickAsync();

            Assert.NotNull(item);
            Assert.Equal("bc1", item.MediaId);
            var debugLine = Assert.Single(logger.Entries, e => e.Level == LogLevel.Debug);
            // bc1 (nudge 0.6) outscores other1 (nudge -0.2) — descending order, matching top3's own.
            Assert.Contains("nudges=[0.60, -0.20]", debugLine.Message);
        }

        [Fact]
        public static async Task TheChipsIncludeARotationChip()
        {
            var (item, _) = await RunWiredPickAsync();

            // The React "why this pick" chip rendering itself lives in
            // admin-ui/__specs__/booth-log-pick-chips.spec.tsx (Jest) — this fact pins the DATA that
            // chip renders from: the winning item's raw nudge reaches MediaItem (SPEC F151.1/F151.2),
            // which BoothLogWriter then threshold-gates (|nudge| >= 0.2, SPEC F151.4) into the
            // persisted stamp the admin UI's rotation chip reads.
            Assert.NotNull(item);
            Assert.Equal(0.6, item.Nudge);
        }
    }
}
