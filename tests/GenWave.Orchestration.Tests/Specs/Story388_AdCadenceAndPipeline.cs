// STORY-388 — An ad airs every N units, from whichever source answers first (F158.2/.3/.5 · PLAN
// T396/T397)
//
// PLAN T396 moved this file's own AC3/AC4/AC6 facts (ScenarioThePipelineOrder, ScenarioAntiRepeat,
// AThrowingSourceIsWarnSkippedAndTheFloorStillAnswers) to GenWave.Ads.Tests: AdSpotPipeline and
// LibraryAdSpotSource — the classes those facts exercise — live in GenWave.Ads, which
// GenWave.Orchestration.Tests does not (and should not) reference. See
// GenWave.Ads.Tests/Specs/Story388_AdSpotPipeline.cs and
// GenWave.Ads.Tests/Specs/Story388_LibraryAdSpotSource.cs — the story tag traveled with them, green
// there. PLAN T397 (this file, now built) is the Orchestrator cadence wiring this pipeline plugs
// into: the deferral, the drain, the KickResolved vend — exercised here entirely through
// GenWave.Core.Abstractions.IAdCadenceProvider/IAdSpotVend fakes, never a real AdSpotPipeline
// (Orchestration.Tests cannot reference GenWave.Ads — the L10 acyclicity call T397 made, see
// IAdSpotVend's own remarks).

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureAdCadenceAndPipeline
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static MediaItem MakeAdSpot(string id = "spot-1") => new(
        id, $"/authored/ads/{id}.wav", $"Spot {id}", new Loudness(-14.0, -1.0, true),
        SegmentKind: SegmentKind.Ad);

    // Every non-ad knob off: the ad cadence trigger is the only thing under test in most facts
    // below (mirrors Story197_SpeechBoundaryDeferral's own CadenceOff idiom).
    static CadenceConfig CadenceOff => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static Orchestrator BuildOrchestrator(
        CadenceConfig cadence,
        SpeechDeferralQueue deferralQueue,
        TimeProvider clock,
        FakeAdCadenceProvider adCadenceProvider,
        FakeAdSpotVend adSpotVend,
        FakeTtsSegmentSource? tts = null,
        ILogger<Orchestrator>? logger = null) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(cadence),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new MusicSelectionPolicy(new FakeMediaCatalog(MakeRef("track")), NullLogger<MusicSelectionPolicy>.Instance),
            tts ?? new FakeTtsSegmentSource(),
            new FakeActivePersonaAccessor(),
            logger ?? NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            deferralQueue,
            clock,
            new FakeBoundaryBiasProvider(TimeSpan.Zero),
            adCadenceProvider: adCadenceProvider,
            adSpotVend: adSpotVend);

    static SpeechDeferralQueue NewQueue(TimeProvider clock) => new(clock);

    static FakeTimeProvider NewClock() => new(DateTimeOffset.Parse("2026-09-01T00:00:00Z"));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCadenceTriggersAVend
    {
        [Fact]
        public async Task TheSecondUnitEnqueuesAnAdDeferral()
        {
            // EveryNUnits=2, a fake IAdSpotVend returning a resolved item: unit 2 enqueues
            // SpeechDeferralKind.Ad (unit 0 never does — the StationId twin's own unitCount > 0
            // guard, mirrored exactly for Ad).
            var clock = NewClock();
            var queue = NewQueue(clock);
            var adCadence = new FakeAdCadenceProvider(2);
            var vend = new FakeAdSpotVend { Answer = MakeAdSpot() };
            var orchestrator = BuildOrchestrator(CadenceOff, queue, clock, adCadence, vend);
            var ctx = new PlayoutContext([]);

            // Unit 0: unitCount > 0 guard fails — no ad, the vend is never even called.
            var first = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(first);
            Assert.Null(first.SegmentKind);
            Assert.Equal(0, vend.CallCount);

            // Unit 1: 1 % 2 != 0 — still no ad.
            var second = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(second);
            Assert.Null(second.SegmentKind);
            Assert.Equal(0, vend.CallCount);

            // Unit 2: 2 % 2 == 0 — the trigger fires, the deferral drains THIS SAME unit
            // (F74.1's queue-not-inline discipline), and the resolved spot airs ahead of the
            // music item it was assembled alongside (KickResolved's own buffer-order guarantee).
            var third = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(third);
            Assert.Equal(SegmentKind.Ad, third.SegmentKind);
            Assert.Equal(1, vend.CallCount);
        }

        [Fact]
        public async Task TheDrainStampsSegmentKindDefensivelyEvenWhenTheVendDoesNot()
        {
            // Review fold: the drain arm stamps SegmentKind.Ad itself (the BuildPooledStationIdItem
            // precedent) — never trusted from the vend alone. A vend that forgets the stamp (here,
            // deliberately unstamped — SegmentKind defaults to null) must still arrive on air
            // correctly tagged, so the render-await loop's own DjName carve-out (kind is
            // SegmentKind.StationId or Announcement or Ad) and every other SegmentKind-keyed reader
            // downstream see the truth regardless of what a future IAdSpotVend implementation does.
            var clock = NewClock();
            var queue = NewQueue(clock);
            var adCadence = new FakeAdCadenceProvider(1);
            var unstamped = MakeAdSpot() with { SegmentKind = null };
            var vend = new FakeAdSpotVend { Answer = unstamped };
            var orchestrator = BuildOrchestrator(CadenceOff, queue, clock, adCadence, vend);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 0 — no trigger
            var ad = await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 1 — fires

            Assert.Equal(SegmentKind.Ad, ad?.SegmentKind);
        }

        [Fact]
        public async Task TheAdDrainsAfterTheStationIdArmAndBeforeTheLeadIn()
        {
            // Assembled unit order: back-announce … station-id, AD, lead-in (F158.3).
            var clock = NewClock();
            var queue = NewQueue(clock);
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = true,
                BackAnnounceAfterEachTrack = true,
                StationIdEveryNUnits = 1,
            };
            var adCadence = new FakeAdCadenceProvider(1);
            var vend = new FakeAdSpotVend { Answer = MakeAdSpot() };
            var orchestrator = BuildOrchestrator(cadence, queue, clock, adCadence, vend);
            var ctx = new PlayoutContext([]);

            // Unit 0: lead-in then the first track — no back-announce (nothing has aired yet) and
            // no station-id/ad (unitCount > 0 guards both). Drained here purely to reach unit 1's
            // own assembly, which is what this fact actually proves.
            await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Unit 1: StationIdEveryNUnits and the ad cadence both fire on the SAME unit —
            // SpeechDeferralKind.Ad's own declared-LAST enum position is what orders the drain
            // "ident → spot" whenever the two coincide (see that member's own remarks); the lead-in
            // itself is a separate, later step (3) in EnqueuePatterAsync, after the whole drain.
            var backAnnounce = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            var stationId = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            var ad = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            var leadIn = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Equal(SegmentKind.BackAnnounce, backAnnounce?.SegmentKind);
            Assert.Equal(SegmentKind.StationId, stationId?.SegmentKind);
            Assert.Equal(SegmentKind.Ad, ad?.SegmentKind);
            Assert.Equal(SegmentKind.LeadIn, leadIn?.SegmentKind);
        }

        [Fact]
        public async Task TheVendIsResolvedNeverRenderedAtAir()
        {
            // The vended item enters via KickResolved — zero synthesizer calls at assembly.
            var clock = NewClock();
            var queue = NewQueue(clock);
            var adCadence = new FakeAdCadenceProvider(1);
            var vend = new FakeAdSpotVend { Answer = MakeAdSpot() };
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(CadenceOff, queue, clock, adCadence, vend, tts);
            var ctx = new PlayoutContext([]);

            // Unit 0 — unitCount > 0 guard, no trigger.
            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Unit 1 — the ad cadence fires; CadenceOff leaves StationId/LeadIn/BackAnnounce all
            // off, so the ad is the ONLY patter item this unit could possibly carry, and it never
            // touches the TTS seam at all.
            var ad = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Equal(SegmentKind.Ad, ad?.SegmentKind);
            Assert.Equal(0, tts.RenderCallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioQuietAndFailingSources
    {
        [Fact]
        public async Task ZeroDisablesTheTriggerEntirely()
        {
            var clock = NewClock();
            var queue = NewQueue(clock);
            var adCadence = new FakeAdCadenceProvider(0);
            var vend = new FakeAdSpotVend { Answer = MakeAdSpot() };
            var orchestrator = BuildOrchestrator(CadenceOff, queue, clock, adCadence, vend);
            var ctx = new PlayoutContext([]);

            for (var i = 0; i < 10; i++)
            {
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotEqual(SegmentKind.Ad, item?.SegmentKind);
            }

            // Not merely "no ad aired" — the trigger's own guard never even calls the vend.
            Assert.Equal(0, vend.CallCount);
        }

        [Fact]
        public async Task AnEmptyPipelineAssemblesTheBreakWithOneInfoNeverAWarn()
        {
            // Null answer = a normal day one (F158.3).
            var clock = NewClock();
            var queue = NewQueue(clock);
            var adCadence = new FakeAdCadenceProvider(1);
            var vend = new FakeAdSpotVend { Answer = null };
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(CadenceOff, queue, clock, adCadence, vend, logger: logger);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 0 — no trigger

            // Unit 1 — the trigger fires, the vend answers null: the break still assembles (the
            // plain music item, no ad segment spliced in), never a fault.
            var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotEqual(SegmentKind.Ad, next?.SegmentKind);
            Assert.Equal(1, vend.CallCount);
            // Never a WARN naming the ad seam — the pre-existing "no CachingScheduleResolver
            // wired" WARN every scheduleResolver-less Orchestrator construction logs once (T124's
            // own scheduleResolverMissingWarned, unrelated to this fact) is expected background
            // noise here, not an ad-specific failure this assertion is scoped to. Matched on the
            // drain arm's OWN throw-path message shape ("Ad spot vend threw...", Ordinal) — never
            // a bare "ad" bigram, which a future unrelated WARN mentioning the word "ad" (in any
            // case) could collide with and falsely red this fact.
            Assert.DoesNotContain(logger.Warnings, w => w.Contains("Ad spot vend", StringComparison.Ordinal));
            Assert.Contains(
                logger.Entries,
                e => e.Level == LogLevel.Information && e.Message.Contains("No ad spot available", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AThrowingVendIsWarnSkippedAndTheUnitStillAssembles()
        {
            // The drain arm's own try/catch boundary (never AdSpotPipeline's — this fact exercises
            // Orchestrator's OWN defense, since a future IAdSpotVend implementation is not
            // guaranteed to share AdSpotPipeline's own never-throws contract): a throwing vend logs
            // exactly one WARN and never faults the unit — the plain music item still airs, with no
            // SegmentKind.Ad spliced in.
            var clock = NewClock();
            var queue = NewQueue(clock);
            var adCadence = new FakeAdCadenceProvider(1);
            var vend = new FakeAdSpotVend { ThrowOnNextCall = new InvalidOperationException("boom") };
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(CadenceOff, queue, clock, adCadence, vend, logger: logger);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 0 — no trigger

            // Unit 1 — the trigger fires, the vend throws: the break still assembles (the plain
            // music item), never a fault out of GetNextAsync itself.
            var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(next);
            Assert.NotEqual(SegmentKind.Ad, next.SegmentKind);
            Assert.Equal(1, vend.CallCount);
            Assert.Single(logger.Warnings, w => w.Contains("Ad spot vend", StringComparison.Ordinal));
        }
    }
}
