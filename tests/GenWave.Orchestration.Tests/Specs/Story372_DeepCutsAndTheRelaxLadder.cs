// STORY-372 — Deep Cuts and the relax ladder (SPEC F152.1, F152.3, F152.4 · PLAN T356, T360, T361)
//
// BDD specification — xUnit. PENDING until T356 (AC1, the Abstractions additive pin), T360
// (AC4–AC6, ShowSummary.Rotation and ScheduleResolver.BuildSegmentEnvelope's block ?? show layering),
// and T361 (AC7–AC9, the R0→R3 relax ladder in MusicSelectionPolicy + the BoothLogWriter
// RotationRelax stamp).
// Arrange sketch: pure in-memory — AC1 constructs SegmentEnvelope directly; AC4/AC6 build a Show/
// block pair by hand and call ScheduleResolver.Resolve, mirroring Story306_IdentityChokepoint.cs's
// own fixture-free style (no store, no Postgres — that half lives in GenWave.MediaLibrary.Tests'
// Story372_TheShowCarriesTheRotationRule.cs). AC5 ("a block's own rule wins") has no real v1 block
// source to drive through that same resolver path — ARCHITECTURE.md's own "Rejected: block-only
// predicate" and SPEC F152.3's "blocks never set it in v1" rule that out by design — so it instead
// pins ScheduleResolver.ResolveRotation's own precedence directly, the one internal chokepoint
// BuildSegmentEnvelope's layering runs through (mirrors EffectiveAssignment.Resolve's own
// "one function, block always wins" shape for persona/show). AC7–AC9 drive MusicSelectionPolicy
// over a fake IMediaCatalog pool sized to force each rung (Story212_EnvelopeProviderAndLadder.cs's
// ladder idiom, ahead of F81.6's rungs).
using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureDeepCutsAndTheRelaxLadder
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the predicate, the layering, and the ladder
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePredicateOnTheEnvelope
    {
        // Given the Abstractions package, When SegmentEnvelope is constructed the pre-5.5.0 way
        // and Rotation is then set.
        static readonly SegmentEnvelope PreExisting =
            new(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], EnergyRange.Unconstrained);

        [Fact]
        public void ThePreExistingConstructorStillCompiles() =>
            Assert.NotNull(PreExisting);

        [Fact]
        public void RotationIsNullByDefault() =>
            Assert.Null(PreExisting.Rotation);

        [Fact]
        public void SettingRotationMaxPlaysZeroIsAdditive()
        {
            var withRotation = PreExisting with { Rotation = new RotationPredicate(MaxPlays: 0) };

            Assert.Equal(PreExisting, withRotation with { Rotation = null });
        }
    }

    public sealed class ScenarioTheShowCarriesTheRule
    {
        // Given a show whose envelope jsonb is {"rotation":{"maxPlays":0}} painted on a block
        // with no rotation, When the schedule envelope is resolved for that block.
        [Fact]
        public void TheEffectiveEnvelopesRotationIsMaxPlaysZero()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;
            var deepCuts = new ShowSummary(1, "Deep Cuts", null, null)
            {
                Rotation = new RotationPredicate(MaxPlays: 0),
            };
            var block = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null, Show: deepCuts);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([block]));

            Assert.Equal(new RotationPredicate(MaxPlays: 0), result.Envelope.Rotation);
        }
    }

    public sealed class ScenarioABlocksOwnRuleWins
    {
        // Given the same show on a block whose envelope has Rotation MaxPlays 2, When resolved.
        //
        // SPEC F152.3 / ARCHITECTURE.md: no block-level rotation source exists, or is even planned,
        // in v1 ("Rejected: block-only predicate — the card can't carry the rule"; "blocks never set
        // it in v1") — segment_schedule/schedule_special carry no rotation column, so there is no way
        // to drive a non-null "block side" through the real ScheduleResolver.Resolve entry point. This
        // fact instead pins the precedence directly on ScheduleResolver.ResolveRotation — the ONE
        // internal chokepoint BuildSegmentEnvelope's own layering runs through (F115.2's "F152.3
        // layering is literally the code" contract) — proving a block-side value, were one to ever
        // exist, wins over the show's, using the exact expression production code composes.
        [Fact]
        public void TheEffectiveRotationIsMaxPlaysTwo()
        {
            var deepCuts = new ShowSummary(1, "Deep Cuts", null, null)
            {
                Rotation = new RotationPredicate(MaxPlays: 0),
            };

            var effective = ScheduleResolver.ResolveRotation(new RotationPredicate(MaxPlays: 2), deepCuts);

            Assert.Equal(new RotationPredicate(MaxPlays: 2), effective);
        }
    }

    public sealed class ScenarioTheDormantFieldsStayDormant
    {
        // Given station.show.persona_id and every non-rotation envelope key hand-populated,
        // When v1 behaviour is exercised.
        //
        // ShowSummary has no member for persona_id or any envelope key beyond Rotation (SPEC F115.2's
        // dormant-columns-unread pin, enforced by that type's own shape — see its remarks); the
        // live-Postgres half proving station.show's own persona_id/other-envelope-keys columns stay
        // unread through the real SQL join is GenWave.MediaLibrary.Tests/Specs/Story240_ScheduleStore.cs's
        // own HandPopulatingShowPersonaIdAndEnvelopeChangesNothingAboutTheLoadedWeek fact. What THIS
        // fact pins is the resolver-side half: a show whose ONLY rotation-relevant fact is "no
        // rotation key present" (Rotation null) carries no other observable difference through
        // ScheduleResolver — every other envelope field on the resolved SegmentEnvelope is exactly
        // what it would be with no show attached at all.
        [Fact]
        public void NothingChangesForTheDormantFields()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;
            var showWithDormantFields = new ShowSummary(1, "Deep Cuts", "tag", "flavor") { Rotation = null };
            var block = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: 7, Genres: ["Rock"], EnergyMin: 0.2, EnergyMax: 0.8, Show: showWithDormantFields);
            var stationDefault = new FakeStationDefaultEnvelopeSource(
                new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Jazz"], new EnergyRange(0.0, 1.0)));
            var resolver = new ScheduleResolver(time, stationDefault);

            var result = resolver.Resolve(new ScheduleWeekSnapshot([block]));

            Assert.Null(result.Envelope.Rotation);
            Assert.Equal(new TimeOnly(9, 0), result.Envelope.StartsAt);
            Assert.Equal(new TimeOnly(12, 0), result.Envelope.EndsAt);
            Assert.Equal(["Rock"], result.Envelope.Genres);
            Assert.Equal(new EnergyRange(0.2, 0.8), result.Envelope.EnergyRange);
        }
    }

    public sealed class ScenarioARotationEditGoesLiveWithoutARestart
    {
        // PLAN T360 review HIGH-1: CachingScheduleResolver's cached ScheduleWeekSnapshot has no TTL —
        // before IShowStore.ShowChanged existed, an operator's rotation edit would sit invisible until
        // an UNRELATED schedule/specials write happened to reload it, or the process restarted. Given
        // a show attached to a block with NO rotation rule yet, resolved once (so the cache is
        // populated and observably stale-safe), When the show's own store raises ShowChanged (modeling
        // ShowRepository.SetRotationAsync's own write) and the underlying "database" now carries a
        // rule, Then the very next ordinary ResolveAsync — no restart, no second lookup by the caller
        // — observes it.
        [Fact]
        public async Task ResolvingAfterShowChangedObservesTheNewRotation()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;
            var showBefore = new ShowSummary(1, "Deep Cuts", null, null);
            var block = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null, Show: showBefore);
            var scheduleStore = new FakeScheduleStore(new ScheduleWeekSnapshot([block]));
            var resolverCore = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));
            var showStore = new FakeShowStore();
            var caching = new CachingScheduleResolver(scheduleStore, resolverCore, new FakeScheduleSpecialStore(), showStore);

            // Before the edit: the cache is populated, Rotation is null.
            var before = await caching.ResolveAsync(CancellationToken.None);
            Assert.Null(before.Envelope.Rotation);

            // The operator edits the show's rotation rule — the real store's own write would raise
            // ShowChanged; this fake models that directly (mirrors FakeScheduleStore.RaiseWeekChanged's
            // own idiom) — and the underlying "database" now carries the new rule (SetSnapshot models
            // what the NEXT LoadWeekAsync would actually read back).
            var showAfter = showBefore with { Rotation = new RotationPredicate(MaxPlays: 0) };
            scheduleStore.SetSnapshot(new ScheduleWeekSnapshot([block with { Show = showAfter }]));
            showStore.RaiseShowChanged();

            var after = await caching.ResolveAsync(CancellationToken.None);

            Assert.Equal(new RotationPredicate(MaxPlays: 0), after.Envelope.Rotation);
            // And the reload actually happened (a genuine cache invalidation, not a coincidence).
            Assert.Equal(2, scheduleStore.LoadWeekAsyncCallCount);
        }
    }

    public sealed class ScenarioTheRelaxLadder
    {
        // Given Rotation MaxPlays 0 and an envelope whose never-aired pool is empty, When a
        // pick runs.
        [Fact(Skip = "pending T361 (STORY-372 AC7)")]
        public void MusicAirsFromR1MaxPlaysOne() => Assert.Fail("pending T361");

        [Fact(Skip = "pending T361 (STORY-372 AC7)")]
        public void ThePickStampCarriesRotationRelaxOne() => Assert.Fail("pending T361");
    }

    public sealed class ScenarioTheLadderReachesTheDecile
    {
        // Given MaxPlays 0 and every row aired at least twice, When a pick runs.
        [Fact(Skip = "pending T361 (STORY-372 AC8)")]
        public void ThePickComesFromTheBottomPlayCountDecileWithRotationRelaxTwo() =>
            Assert.Fail("pending T361");
    }

    public sealed class ScenarioR3IsStampedNeverSilent
    {
        // Given a library where every rotation step yields nothing, When a pick runs.
        [Fact(Skip = "pending T361 (STORY-372 AC9)")]
        public void MusicAirsWithRotationRelaxThree() => Assert.Fail("pending T361");

        [Fact(Skip = "pending T361 (STORY-372 AC9)")]
        public void OneLogLinePerAiringNamesTheStep() => Assert.Fail("pending T361");
    }
}
