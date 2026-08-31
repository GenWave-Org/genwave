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
// pins ScheduleSegment.EffectiveEnvelope's own shipped layering directly (T376 review round-3: the
// prior ScheduleResolver.ResolveRotation chokepoint this fact used is gone — it had zero production
// callers once EffectiveEnvelope shipped, so it was deleted rather than kept as untested dead code),
// proving the show's own rotation rides straight through with no block-side source at all. AC7–AC9
// drive MusicSelectionPolicy
// over a fake IMediaCatalog pool sized to force each rung (Story212_EnvelopeProviderAndLadder.cs's
// ladder idiom, ahead of F81.6's rungs).
//
// T361 REVIEW (opus, HIGH-1/HIGH-2/LOW-4): the per-R-step attempt now tries the persona pick THEN
// the un-relaxed envelope-only pick (MusicSelectionPolicy.TryRotationStepAsync) — a persona-less
// pick (BuildRotationOrchestrator's withPersona:false) must ALSO honour the predicate through the
// SAME rotation-aware fake (FakeRotationLadderPoolCatalog), not just a real ranker. Two new facts
// (ScenarioTheEnvelopeOnlyPathHonoursThePredicate) prove that directly; ScenarioR3IsStampedNeverSilent
// (AC9) is rewritten with a real persona bound and a genre mismatch (the ONLY way R2's own quantile
// read can genuinely answer null against a non-empty catalog — percentile_disc always returns an
// actually-observed value, so a high play count alone can never make R2 fail on its own; see that
// scenario's own remarks) instead of FakeLadderMediaCatalog, which ignored envelope.Rotation
// entirely and let AC9 pass for the wrong reason. ScenarioNoRuleNoStampEndToEnd (LOW-4) proves
// MediaItem.RotationRelax stays null end-to-end for a rule-less pick; the TrackAired/booth-log half
// of that same null is GenWave.MediaLibrary.Tests/Specs/Story372_ThePoolHonoursTheRotationPredicate.cs's
// own RotationRelaxIsAbsentFromEveryStamp fact.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureDeepCutsAndTheRelaxLadder
{
    // ---------------------------------------------------------------------
    // Helpers (AC7–AC9)
    // ---------------------------------------------------------------------

    static MediaReference MakeRef(string id, string? genre) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: genre,
        Year: null);

    static CadenceConfig SilentCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    /// <summary>
    /// Wires the REAL production pick path — <see cref="Orchestrator.GetNextAsync"/> over
    /// <paramref name="catalog"/> — so the SPEC F152.4 relax ladder's own attempts genuinely depend
    /// on the catalog's rotation-aware answers, mirroring Story212/213's own "prove the ladder as
    /// behavior of the production pick path" discipline. <paramref name="withPersona"/> selects
    /// between a REAL <see cref="RankerPersonaPickProvider"/>/<see cref="PersonaRanker"/> (rung 0,
    /// taste-rule-free — AC7/AC8/AC9 only care which envelope's pool query admitted a row, never
    /// taste scoring) and the DEFAULT <see cref="NoOpPersonaPickProvider"/> binding (T361 review
    /// HIGH-1: the common persona-less case — <see cref="MusicSelectionPolicy"/> constructed with no
    /// persona provider at all — which must ALSO honour the F152 predicate through the envelope-only
    /// fallback, not skip the ladder entirely).
    /// </summary>
    static (Orchestrator Orchestrator, CapturingLogger<MusicSelectionPolicy> Logger) BuildRotationOrchestrator(
        IMediaCatalog catalog, SegmentEnvelope envelope, bool withPersona)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(SilentCadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings { ArtistSeparation = 0 });

        IActivePersonaAccessor personaAccessor = new FakeActivePersonaAccessor();
        IPersonaPickProvider? provider = null;
        if (withPersona)
        {
            var persona = new Persona(7, "DJ Test", "", "", "", DateTime.UnixEpoch, DateTime.UnixEpoch);
            var card = new PersonaCard(1, "DJ Test", "", "", [], new VoiceSpec("kokoro", "", 1.0, "en"), EnergyDisposition: 0.0, [], []);
            var boundAccessor = new FakeActivePersonaAccessor { Persona = persona, Card = card };
            personaAccessor = boundAccessor;
            var ranker = new PersonaRanker(
                new FakePersonaTasteReader([]), new SeededRandomSource(seed: 1), TimeProvider.System,
                new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);
            provider = new RankerPersonaPickProvider(catalog, boundAccessor, ranker, new PersonaRankerOptions());
        }

        var policyLogger = new CapturingLogger<MusicSelectionPolicy>();
        var musicSelectionPolicy = new MusicSelectionPolicy(
            catalog, policyLogger, new FakeEnvelopeProvider(envelope), provider);
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy,
            new FakeTtsSegmentSource(), personaAccessor, NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero));
        return (orchestrator, policyLogger);
    }

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

    public sealed class ScenarioTheShowsRotationRidesThroughEffectiveEnvelope
    {
        // Given a show carrying a rotation rule on a block, When ScheduleSegment.EffectiveEnvelope
        // resolves that block.
        //
        // SPEC F152.3 / ARCHITECTURE.md: no block-level rotation source exists, or is even planned,
        // in v1 ("Rejected: block-only predicate — the card can't carry the rule"; "blocks never set
        // it in v1") — segment_schedule/schedule_special carry no rotation column, and
        // EffectiveEnvelope's own shipped formula (T376 review MED-4) reflects that directly:
        // Rotation = Show?.Rotation, no block-side parameter at all.
        //
        // T376 review round-3 (RULED): the prior fact pinned this through ScheduleResolver.ResolveRotation
        // — an internal "block ?? show" chokepoint that no longer has ANY production caller once
        // EffectiveEnvelope shipped (BuildSegmentEnvelope now delegates to it directly) — deleted
        // rather than left as untested dead code. This fact pins the SAME shipped behaviour at its
        // real, current source instead: the show's own rotation rides straight through.
        [Fact]
        public void TheEffectiveRotationIsTheShowsOwn()
        {
            var deepCuts = new ShowSummary(1, "Deep Cuts", null, null)
            {
                Rotation = new RotationPredicate(MaxPlays: 2),
            };
            var block = new ScheduleSegment(
                Id: 1, Day: DayOfWeek.Monday, StartMinute: 0, EndMinute: 30,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null, Show: deepCuts);

            var effective = block.EffectiveEnvelope(SegmentEnvelope.StationDefault);

            Assert.Equal(new RotationPredicate(MaxPlays: 2), effective.Rotation);
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
        // Given Rotation MaxPlays 0 and an envelope whose never-aired pool is empty (the one row on
        // the shelf already aired once, failing R0's own MaxPlays 0), When a pick runs.
        static async Task<MediaItem?> RunAsync()
        {
            var media = MakeRef("aired-once", "Rock");
            var catalog = new FakeRotationLadderPoolCatalog([new FakeRotationLadderPoolCatalog.Row(media, PlayCount: 1)]);
            var envelope = SegmentEnvelope.StationDefault with { Rotation = new RotationPredicate(MaxPlays: 0) };
            var (orchestrator, _) = BuildRotationOrchestrator(catalog, envelope, withPersona: true);

            return await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
        }

        [Fact]
        public async Task MusicAirsFromR1MaxPlaysOne()
        {
            var item = await RunAsync();

            Assert.NotNull(item);
            Assert.Equal("aired-once", item.MediaId);
        }

        [Fact]
        public async Task ThePickStampCarriesRotationRelaxOne()
        {
            var item = await RunAsync();

            Assert.NotNull(item);
            Assert.Equal(1, item.RotationRelax);
        }
    }

    public sealed class ScenarioTheLadderReachesTheDecile
    {
        // Given MaxPlays 0 and every row aired at least twice — four rows at play_count 2, one
        // heavily-played outlier at 50 — When a pick runs. R0 (<=0) and R1 (<=1) both admit nothing
        // (every row's play_count is >= 2); R2's own bottom-decile read (percentile_disc(0.1) over
        // [2,2,2,2,50] = 2) narrows MaxPlays to 2, admitting the four low rows and excluding the
        // outlier.
        [Fact]
        public async Task ThePickComesFromTheBottomPlayCountDecileWithRotationRelaxTwo()
        {
            var lowIds = new[] { "low-1", "low-2", "low-3", "low-4" };
            var rows = lowIds
                .Select(id => new FakeRotationLadderPoolCatalog.Row(MakeRef(id, "Rock"), PlayCount: 2))
                .Append(new FakeRotationLadderPoolCatalog.Row(MakeRef("outlier", "Rock"), PlayCount: 50))
                .ToList();
            var catalog = new FakeRotationLadderPoolCatalog(rows);
            var envelope = SegmentEnvelope.StationDefault with { Rotation = new RotationPredicate(MaxPlays: 0) };
            var (orchestrator, _) = BuildRotationOrchestrator(catalog, envelope, withPersona: true);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal(2, item.RotationRelax);
            Assert.Contains(item.MediaId, lowIds);
        }
    }

    public sealed class ScenarioR3IsStampedNeverSilent
    {
        // T361 review HIGH-2: rewritten with FakeRotationLadderPoolCatalog (rotation-AWARE, unlike
        // the old FakeLadderMediaCatalog, which ignored envelope.Rotation entirely and let this
        // scenario pass for the wrong reason — every R step "failed" only because no persona was
        // bound, not because the catalog genuinely evaluated the predicate) and a REAL persona bound
        // (withPersona: true), so R0/R1 fail for a REAL reason: the one row's play_count (100) badly
        // exceeds both MaxPlays 0 and MaxPlays 1.
        //
        // R2 needs a SECOND, independent reason to fail too: percentile_disc always returns an
        // ACTUALLY-OBSERVED value, so a non-empty (genre-matching) pool always admits at least the
        // row at the computed percentile — a lone high-play-count row can never make R2 fail purely
        // on play count, because its own play_count trivially becomes p10 for a single-row pool. The
        // envelope's Genres = ["Jazz"] while the row's own genre is "Rock" supplies that second
        // reason: it excludes the row from R0-R3's OWN genre-constrained pool (and from R2's
        // genre-matching quantile read, which then answers null — "nothing to compute", correctly
        // skipping R2 rather than fabricating a MaxPlays: 0). Only SelectEnvelopeLadderAsync's OWN,
        // untouched F81.6 rung 4 (relax genres to admit everything) finally reaches the row — proving
        // "never silence" AND that RotationRelax pins to 3 regardless of which F81.6 rung resolved it.
        static async Task<(MediaItem? Item, CapturingLogger<MusicSelectionPolicy> Logger)> RunAsync()
        {
            var media = MakeRef("aired-often", "Rock");
            var catalog = new FakeRotationLadderPoolCatalog([new FakeRotationLadderPoolCatalog.Row(media, PlayCount: 100)]);
            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Jazz"], EnergyRange.Unconstrained)
            {
                Rotation = new RotationPredicate(MaxPlays: 0),
            };
            var (orchestrator, policyLogger) = BuildRotationOrchestrator(catalog, envelope, withPersona: true);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            return (item, policyLogger);
        }

        [Fact]
        public async Task MusicAirsWithRotationRelaxThree()
        {
            var (item, _) = await RunAsync();

            Assert.NotNull(item);
            Assert.Equal("aired-often", item.MediaId);
            Assert.Equal(3, item.RotationRelax);
        }

        [Fact]
        public async Task OneLogLinePerAiringNamesTheStep()
        {
            var (_, logger) = await RunAsync();

            var relaxLines = logger.Entries.Where(e =>
                e.Level == LogLevel.Information && e.Message.Contains("Rotation rule relaxed", StringComparison.Ordinal));

            var line = Assert.Single(relaxLines);
            Assert.Contains("step 3", line.Message);
        }
    }

    public sealed class ScenarioTheEnvelopeOnlyPathHonoursThePredicate
    {
        // T361 review HIGH-1: MusicSelectionPolicy.TryRotationStepAsync's SECOND leg — the un-relaxed
        // IMediaCatalog.GetEnvelopeCandidateAsync pick — is what a persona-less pick (the DEFAULT
        // NoOpPersonaPickProvider binding: no ranker ever wired up, F91 music-only segments, a
        // schedule gap, or a persona-resolve fault) now falls back to at EVERY R step, rather than
        // skipping the whole ladder to the F81.6 fallback with Rotation already dropped. Both facts
        // below build the orchestrator with withPersona: false — no RankerPersonaPickProvider at all
        // — so rung 0 answers "no opinion" unconditionally and ONLY the envelope-only leg can
        // possibly be what picks the row.

        // Given a persona-less pick with Rotation MaxPlays 0 and a never-aired row present, When a
        // pick runs, Then the envelope-only path itself picks that row directly, unrelaxed (R0).
        [Fact]
        public async Task APersonaLessPickWithANeverAiredRowPicksItAtRotationRelaxZero()
        {
            var media = MakeRef("never-aired", "Rock");
            var catalog = new FakeRotationLadderPoolCatalog([new FakeRotationLadderPoolCatalog.Row(media, PlayCount: 0)]);
            var envelope = SegmentEnvelope.StationDefault with { Rotation = new RotationPredicate(MaxPlays: 0) };
            var (orchestrator, _) = BuildRotationOrchestrator(catalog, envelope, withPersona: false);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("never-aired", item.MediaId);
            Assert.Equal(0, item.RotationRelax);
        }

        // Given a persona-less pick whose never-aired pool is empty (the one row already aired
        // once), When a pick runs, Then the envelope-only path relaxes to R1 (MaxPlays 1), stamped 1.
        [Fact]
        public async Task APersonaLessPickWhoseNeverAiredPoolIsEmptyLandsAtRotationRelaxOne()
        {
            var media = MakeRef("aired-once", "Rock");
            var catalog = new FakeRotationLadderPoolCatalog([new FakeRotationLadderPoolCatalog.Row(media, PlayCount: 1)]);
            var envelope = SegmentEnvelope.StationDefault with { Rotation = new RotationPredicate(MaxPlays: 0) };
            var (orchestrator, _) = BuildRotationOrchestrator(catalog, envelope, withPersona: false);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("aired-once", item.MediaId);
            Assert.Equal(1, item.RotationRelax);
        }
    }

    public sealed class ScenarioNoRuleNoStampEndToEnd
    {
        // T361 review LOW-4: a rule-less pick (envelope.Rotation null) carries RotationRelax null all
        // the way onto the playout-facing MediaItem through the REAL Orchestrator.GetNextAsync path —
        // proving the byte-identical no-rotation path never accidentally stamps a member. The
        // TrackAired/booth-log half of this SAME null is proven by
        // GenWave.MediaLibrary.Tests/Specs/Story372_ThePoolHonoursTheRotationPredicate.cs's own
        // RotationRelaxIsAbsentFromEveryStamp fact (a hand-built TrackAired, no Orchestrator needed
        // there since TrackAired is produced downstream by PlayoutFeeder, not by GetNextAsync itself).
        [Fact]
        public async Task MediaItemRotationRelaxIsNullForARuleLessPick()
        {
            var media = MakeRef("plain", "Rock");
            var catalog = new FakeRotationLadderPoolCatalog([new FakeRotationLadderPoolCatalog.Row(media, PlayCount: 0)]);
            var (orchestrator, _) = BuildRotationOrchestrator(catalog, SegmentEnvelope.StationDefault, withPersona: false);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Null(item.RotationRelax);
        }
    }
}
