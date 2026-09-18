// STORY-453 — The break contract has one fact per cell (gh-#401 · SPEC F186.3 · PLAN T518)
//
// AC18–AC23 — the drop cells, the third of three files FeatureBreakContract spans (AC1–AC9 live in
// Story453_BreakContract.cs, AC10–AC17/AC24 in Story453_BreakContractSlots.cs; this file exists
// purely for the ~300-line budget, csharp-best-practices). Shares those files' consts/helpers
// (PullAsync, PullManyAsync, Cadence, BuildCrosstalkChain, StockReadyExchange, Handoff, ShowSlug)
// via the partial class. Every fact below was un-skipped from the SAD PATH block
// Story453_BreakContractSlots.cs used to carry (PendingDrops, now deleted there — nothing left
// referencing it).
//
// Mirrors AC1–AC17's own arrange discipline: each scenario builds only what its own Given names,
// through OrchestratorBuilder/OrchestratorChain against the real, unsplit Orchestrator. AC20/AC21
// each arrange the SAME full-break slot order SPEC F186 names (BackAnnounce, Announcement,
// StationId, LeadIn, Track) and assert the buffered kinds equal that order with the dropped kind
// removed — one Assert.Equal on two lists, never a second run of the same unit to diff against.

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Orchestration.Tests.Specs;

public static partial class FeatureBreakContract
{
    /// <summary>
    /// AC21's own race: advances <paramref name="clock"/> in <paramref name="renderBudget"/> steps
    /// (the Story243/Story442 PullUnitsRacingTheRenderBudgetAsync idiom, drop case) so a render
    /// whose delay exceeds the budget loses deterministically, then keeps yielding until the rest of
    /// the unit's own renders (synchronous fakes, no clock dependency) finish assembling — no real
    /// wall-clock wait either way.
    /// </summary>
    static async Task<MediaItem> PullOverBudgetAsync(Orchestrator orchestrator, FakeTimeProvider clock, TimeSpan renderBudget)
    {
        var pull = orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
        for (var round = 0; round < 8 && !pull.IsCompleted; round++)
        {
            clock.Advance(renderBudget);
            for (var spin = 0; spin < 50 && !pull.IsCompleted; spin++)
                await Task.Yield();
        }

        var item = await pull;
        Assert.NotNull(item);
        return item;
    }

    public sealed class ScenarioAnExpiredTimeDate : IAsyncLifetime
    {
        readonly FakeTtsSegmentSource tts = new();
        readonly CapturingLogger<Orchestrator> logger = new();

        // Given: TimeDate due 120 s ago with a 90 s budget
        public async Task InitializeAsync()
        {
            var imaging = new FakeStationImagingSettingsProvider { Current = new StationImagingSettings(false, false, 90) };
            var chain = new OrchestratorBuilder().WithTts(tts).WithImagingSettings(imaging).WithLogger(logger).Build();
            chain.Queue.Enqueue(SpeechDeferralKind.TimeDate, "test: expired time due", chain.Time.GetUtcNow() - TimeSpan.FromSeconds(120));

            await PullAsync(chain.Orchestrator);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC18 — no TimeDate render is ever requested</summary>
        [Fact]
        public void BuffersNoTimeDate() => Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.TimeDate);

        /// <summary>AC18 — the expiry line names the drop</summary>
        [Fact]
        public void LogsOneExpiryLine() => Assert.Single(logger.Warnings, w => w.Contains("dropped undrained", StringComparison.Ordinal));
    }

    public sealed class ScenarioAnAdVendThatThrows : IAsyncLifetime
    {
        readonly CapturingLogger<Orchestrator> logger = new();
        List<MediaItem> items = [];

        // Given: the vend throws
        public async Task InitializeAsync()
        {
            var adCadence = new FakeAdCadenceProvider(1);
            var adSpotVend = new FakeAdSpotVend { ThrowOnNextCall = new InvalidOperationException("Simulated ad vend fault (test double).") };
            var chain = new OrchestratorBuilder().WithAdCadence(adCadence).WithAdSpotVend(adSpotVend).WithLogger(logger).Build();

            await PullAsync(chain.Orchestrator); // unit 0 — the ad cadence has not hit yet
            items = await PullManyAsync(chain.Orchestrator, 1); // unit 1 — cadence hits, the vend throws
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC19 — the break assembles without an ad</summary>
        [Fact]
        public void AssemblesTheBreakWithoutAnAd() => Assert.DoesNotContain(items, i => i.SegmentKind == SegmentKind.Ad);

        /// <summary>AC19 — one WARN names the vend</summary>
        [Fact]
        public void LogsOneWarnNamingTheVend() => Assert.Single(logger.Warnings, w => w.Contains("Ad spot vend threw", StringComparison.Ordinal));
    }

    public sealed class ScenarioANullLeadInRender : IAsyncLifetime
    {
        List<MediaItem> items = [];

        // Given: the lead-in render returns null, inside a full break (back-announce, announcement,
        // station id, lead-in, track — the SPEC F186 slot order)
        public async Task InitializeAsync()
        {
            var cadence = Cadence(leadIn: true, backAnnounce: true, stationIdEvery: 1);
            var source = new FakeAnnouncementSource();
            var renderer = new FakeVerbatimSegmentRenderer();
            var tts = new FakeTtsSegmentSource { ShouldReturnNull = r => r.Kind == SegmentKind.LeadIn };
            var chain = new OrchestratorBuilder()
                .WithCadence(cadence).WithTts(tts).WithAnnouncementSource(source).WithAnnouncementRenderer(renderer).Build();

            await PullAsync(chain.Orchestrator); // unit 0 — primes the previous track, cadence not hit yet

            source.Pending.Enqueue(new AnnouncementItem(1, "One", true, null));
            items = await PullManyAsync(chain.Orchestrator, 4); // unit 1 — the full break, lead-in drops alone
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC20 — the slot drops alone</summary>
        [Fact]
        public void KeepsEveryOtherItemInPosition()
        {
            var fullBreak = new List<SegmentKind?> { SegmentKind.BackAnnounce, SegmentKind.Announcement, SegmentKind.StationId, SegmentKind.LeadIn, null };
            Assert.Equal(fullBreak.Where(kind => kind != SegmentKind.LeadIn), items.Select(i => i.SegmentKind));
        }
    }

    public sealed class ScenarioAnOverBudgetBackAnnounce : IAsyncLifetime
    {
        List<MediaItem> items = [];

        // Given: the back-announce render exceeds the budget, inside a break of back-announce +
        // announcement + track (no station id/lead-in — isolates the race to the ONE tts render this
        // unit makes)
        public async Task InitializeAsync()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
            var cadence = Cadence(leadIn: false, backAnnounce: true, stationIdEvery: 0);
            var source = new FakeAnnouncementSource();
            var renderer = new FakeVerbatimSegmentRenderer();
            var tts = new FakeTtsSegmentSource { TimeProvider = clock };
            var renderBudget = TimeSpan.FromSeconds(5);
            var chain = new OrchestratorBuilder()
                .WithTime(clock).WithCadence(cadence).WithTts(tts)
                .WithAnnouncementSource(source).WithAnnouncementRenderer(renderer)
                .WithRenderBudget(renderBudget)
                .Build();

            await PullAsync(chain.Orchestrator); // unit 0 — primes the previous track

            source.Pending.Enqueue(new AnnouncementItem(1, "One", true, null));
            tts.RenderDelay = renderBudget + TimeSpan.FromSeconds(5); // exceeds the budget
            var first = await PullOverBudgetAsync(chain.Orchestrator, clock, renderBudget); // unit 1 — the back-announce races the budget
            items = [first, .. await PullManyAsync(chain.Orchestrator, 1)];
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC21 — the slot drops alone</summary>
        [Fact]
        public void KeepsEveryOtherItemInPosition()
        {
            var fullBreak = new List<SegmentKind?> { SegmentKind.BackAnnounce, SegmentKind.Announcement, null };
            Assert.Equal(fullBreak.Where(kind => kind != SegmentKind.BackAnnounce), items.Select(i => i.SegmentKind));
        }
    }

    public sealed class ScenarioANullSignOffRender : IAsyncLifetime
    {
        readonly CapturingStationEventSink events = new();

        // Given: the SignOff render returns null
        public async Task InitializeAsync()
        {
            var tts = new FakeTtsSegmentSource { ShouldReturnNull = r => r.Kind == SegmentKind.SignOff };
            var chain = new OrchestratorBuilder().WithTts(tts).WithEvents(events).Build();
            chain.Queue.Enqueue(SpeechDeferralKind.SignOff, "test: null render signoff", chain.Time.GetUtcNow(), Handoff);

            await PullAsync(chain.Orchestrator);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC22 — one HandoffPieceDropped event is published</summary>
        [Fact]
        public void PublishesOneHandoffPieceDropped() => Assert.Single(events.Events, evt => evt.GetType().Name == "HandoffPieceDropped");
    }

    public sealed class ScenarioANullAnnouncementRender : IAsyncLifetime
    {
        readonly FakeAnnouncementSource source = new();

        // Given: an announcement render returns null
        public async Task InitializeAsync()
        {
            source.Pending.Enqueue(new AnnouncementItem(9001, "Message", true, null));
            var renderer = new FakeVerbatimSegmentRenderer { AlwaysReturnNull = true };
            var chain = new OrchestratorBuilder().WithAnnouncementSource(source).WithAnnouncementRenderer(renderer).Build();

            await PullAsync(chain.Orchestrator);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC23 — the claim is not released. IAnnouncementSource has no release method, so an
        /// empty Pending (dequeued, never put back) is the only observable "still claimed" signal; a split
        /// that adds a release path must extend the fake and this fact together.</summary>
        [Fact]
        public void KeepsTheAnnouncementClaimed() => Assert.Empty(source.Pending);
    }
}
