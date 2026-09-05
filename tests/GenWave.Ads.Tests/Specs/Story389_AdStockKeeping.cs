// STORY-389 — The stock keeps itself (worker half: AC2–AC5 · F159.3/.4 · PLAN T402)

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

public static class FeatureAdStockKeeping
{
    static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    // A well-formed 30s spot the REAL AdScriptValidator accepts end to end (Story390_AdScriptWriterMeetsTheRealValidator's
    // own proven reply — ANNOUNCER-led, a second voice, a 555 number, comfortably under the 42s ceiling).
    const string WellFormedReply =
        "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
        "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
        "ANNOUNCER: Call 555-0142 - that's 555-0142 - Cravin's Diner.";

    static Dictionary<string, string?> Settings(int? targetCount = null, int? refreshDays = null, bool? autoApprove = null)
    {
        var settings = new Dictionary<string, string?>();
        if (targetCount is { } t) settings["Station:Ads:TargetCount"] = t.ToString();
        if (refreshDays is { } r) settings["Station:Ads:RefreshDays"] = r.ToString();
        if (autoApprove is { } a) settings["Station:Ads:AutoApprove"] = a ? "true" : "false";
        return settings;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDraftByDefault
    {
        [Fact]
        public async Task AGeneratedSpotLandsInDraftWhenAutoApproveIsOff()
        {
            // Given Station:Ads:AutoApprove=false (the default) and one enabled brief...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(autoApprove: false), llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When generation completes (the stock pass, below target by default: 0 ready < 12)...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the spot lands in draft.
            Assert.Equal(1, harness.Store.CreateCallCount);
            Assert.Equal(AdState.Draft, harness.Store.CreateRequests.Single().InitialState);
        }

        [Fact]
        public async Task TheWorkerNeverRendersADraft()
        {
            // Given a spot already sitting in draft...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Draft);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then it was never claimed or rendered — still draft, no MarkReady/MarkFailed call at all.
            Assert.Equal(AdState.Draft, Assert.Single(harness.Store.Spots).State);
            Assert.Equal(0, harness.Store.MarkReadyCallCount);
            Assert.Equal(0, harness.Store.MarkFailedCallCount);
        }
    }

    public sealed class ScenarioAutoApproveFlowsThrough
    {
        [Fact]
        public async Task AGeneratedSpotLandsInApprovedWhenAutoApproveIsOn()
        {
            // Given AutoApprove=true and one enabled brief...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(autoApprove: true), llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When generation completes...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the spot lands in approved — render-eligible immediately (never draft).
            Assert.Equal(1, harness.Store.CreateCallCount);
            Assert.Equal(AdState.Approved, harness.Store.CreateRequests.Single().InitialState);
        }
    }

    public sealed class ScenarioRefreshRetiresAndRefills
    {
        [Fact]
        public async Task AStaleLlmSpotIsRetiredWithItsMediaRowIneligible()
        {
            // Given TargetCount=2, RefreshDays=30, and a ready llm spot older than 30 days...
            var harness = AdSpotWorkerHarness.Build(Now, Settings(targetCount: 2, refreshDays: 30));
            harness.Store.AddSpot(
                1, AdState.Ready, AdSource.Llm, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddDays(-31));

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the stale spot is retired and its media row is set ineligible via SetEligibleAsync
            // (PLAN T402 review block 3) — never deleted, the row still exists.
            var spot = Assert.Single(harness.Store.Spots);
            Assert.Equal(AdState.Retired, spot.State);
            Assert.Contains((500L, false), harness.CatalogWriter.SetEligibleHistory);
        }

        [Fact]
        public async Task GenerationRefillsTowardTargetCount()
        {
            // Given TargetCount=2, zero ready spots, and one enabled brief...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(targetCount: 2), llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then generation refills TOWARD the target — one attempt per tick, never a catch-up burst.
            Assert.Equal(1, harness.Store.CreateCallCount);
        }
    }

    public sealed class ScenarioTheStockCountSpansThePipeline
    {
        // gh-#689: the first cut counted the READY shelf alone. Under AutoApprove=false (the shipped
        // default) a generated spot waits in draft and never reached that count, so every tick wrote
        // one more draft, forever. The stock count is now draft|approved|rendering|ready, llm/pack.

        [Fact]
        public async Task AWaitingDraftCountsTowardTheTarget()
        {
            // Given TargetCount=1, AutoApprove=false, one llm draft already waiting for the owner's
            // eye, and an enabled brief (kills the ready-shelf-only count, which never saw the draft)...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(targetCount: 1, autoApprove: false),
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Store.AddSpot(1, AdState.Draft, AdSource.Llm);
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then nothing new is generated — the draft IS the stock, on its way; no brief was sampled.
            Assert.Equal(0, harness.Store.CreateCallCount);
            Assert.Equal(0, harness.Briefs.SampleCallCount);
        }

        [Fact]
        public async Task ARenderingSpotCountsTowardTheTarget()
        {
            // Given TargetCount=1, one llm spot mid-render (well inside the guardian grace), and an
            // enabled brief...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(targetCount: 1),
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Store.AddSpot(1, AdState.Rendering, AdSource.Llm, stateChangedAt: Now.UtcDateTime.AddMinutes(-1));
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then nothing new is generated — a render in flight is stock on its way too.
            Assert.Equal(0, harness.Store.CreateCallCount);
        }

        [Fact]
        public async Task AFailedSpotNeverBlocksRefill()
        {
            // Given TargetCount=1, one llm spot in failed (waiting on an operator retry or discard),
            // and an enabled brief...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(targetCount: 1),
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Store.AddSpot(1, AdState.Failed, AdSource.Llm, failReason: "format");
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then one generation happens — failed is never stock.
            Assert.Equal(1, harness.Store.CreateCallCount);
        }

        [Fact]
        public async Task AnOwnerDraftNeverCountsTowardTheTarget()
        {
            // Given TargetCount=1, one OWNER draft, and an enabled brief (SPEC F159.3: owner spots
            // never count toward the target)...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(targetCount: 1),
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Store.AddSpot(1, AdState.Draft, AdSource.Owner);
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then one generation happens — the owner's draft is theirs, not the worker's stock.
            Assert.Equal(1, harness.Store.CreateCallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheExemptAndTheStuck
    {
        [Fact]
        public async Task AnOwnerSpotIsNeverRefreshRetired()
        {
            // Given a ready source=owner spot older than RefreshDays...
            var harness = AdSpotWorkerHarness.Build(Now, Settings(targetCount: 2, refreshDays: 30));
            harness.Store.AddSpot(
                1, AdState.Ready, AdSource.Owner, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddDays(-90));

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then it is not retired — owner spots retire only by hand.
            Assert.Equal(0, harness.Store.RetireCallCount);
            Assert.Equal(AdState.Ready, Assert.Single(harness.Store.Spots).State);
        }

        [Fact]
        public async Task AStuckRenderingSpotReArmsToApprovedAfterTheGrace()
        {
            // Given a spot stuck in rendering well past the guardian's own grace (RenderBudgetSeconds
            // (300s, this harness's own test default) + AdSpotGuardianGrace.Margin (2 minutes) = 7
            // minutes)...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Rendering, stateChangedAt: Now.UtcDateTime.AddMinutes(-8));

            // When the guardian sweeps...
            await harness.Guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the spot returns to approved for retry — the announcements re-arm shape.
            Assert.Equal(AdState.Approved, Assert.Single(harness.Store.Spots).State);
            Assert.Equal(1, harness.Store.ReArmCallCount);
        }

        [Fact]
        public async Task ARenderingSpotStuckLessThanTheGraceIsLeftAlone()
        {
            // Given a spot stuck 6 minutes — inside the REAL 7-minute grace (RenderBudgetSeconds 300s
            // + AdSpotGuardianGrace.Margin 2min) but OUTSIDE a mutated 5-minute one
            // (RenderBudgetSeconds alone, Margin zeroed) — chosen deliberately BETWEEN the two so this
            // fact actually discriminates them (PLAN T402 review F2(1) — kills a mutant that zeroes
            // AdSpotGuardianGrace.Margin, which would re-arm a render genuinely still in flight)...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Rendering, stateChangedAt: Now.UtcDateTime.AddMinutes(-6));

            // When the guardian sweeps...
            await harness.Guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then it is left alone — a render this young could still be honestly in flight.
            Assert.Equal(AdState.Rendering, Assert.Single(harness.Store.Spots).State);
            Assert.Equal(0, harness.Store.ReArmCallCount);
        }
    }

    public sealed class ScenarioTheRepairSweep
    {
        [Fact]
        public async Task ARecentReadySpotsIneligibleMediaRowIsRepaired()
        {
            // Given a ready spot whose OWN ready transition happened moments ago (well inside the
            // guardian grace) and whose media row is still ineligible — the exact MarkReadyAsync-
            // committed/SetEligibleAsync-never-ran race the repair sweep exists to close (PLAN T402
            // review F1/F4, F2(2))...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 900, stateChangedAt: Now.UtcDateTime.AddMinutes(-1));
            harness.AdminLookup.Add(900, AdSpotWorkerHarness.MakeMediaRow(900, eligible: false), harness.AdsLibraryId);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the repair sweep flips it eligible.
            Assert.Contains((900L, true), harness.CatalogWriter.SetEligibleHistory);
        }

        [Fact]
        public async Task ARealisticOrphanIsReachableAtProductionDefaults()
        {
            // Given a ready spot 11 minutes old — OLDER than the production Ads:WorkerIntervalMinutes
            // (10, this harness's own default, matching AdsOptions' own shipped default) but still
            // inside the repair window PLAN T402 review F6 widens to WorkerIntervalMinutes + the
            // guardian's own grace (10min + (RenderBudgetSeconds 180s + AdSpotGuardianGrace.Margin
            // 2min) = 15min at PRODUCTION RenderBudgetSeconds, not this harness's own 300s test
            // default) — the exact "born mid-tick, first observable one full tick later" shape review
            // F6 exists to prove genuinely reachable, not merely sized on paper...
            var harness = AdSpotWorkerHarness.Build(Now, renderBudgetSeconds: 180);
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 903, stateChangedAt: Now.UtcDateTime.AddMinutes(-11));
            harness.AdminLookup.Add(903, AdSpotWorkerHarness.MakeMediaRow(903, eligible: false), harness.AdsLibraryId);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then it is repaired — the window resize actually closes the "unreachable at production
            // defaults" gap PLAN T402 review F6 found, not merely widens it on paper.
            Assert.Contains((903L, true), harness.CatalogWriter.SetEligibleHistory);
        }

        [Fact]
        public async Task AnAlreadyEligibleRecentRowDrawsNoWrite()
        {
            // Given a ready spot inside the repair window whose media row is ALREADY eligible — the
            // common case, every ordinary tick a spot spends Ready (PLAN T402 review N1 — kills a
            // mutant that drops the "|| found.Value.Row.Eligible" guard, which would re-write
            // eligible=true on EVERY tick this row stays Ready, churning its own xmin/ETag for
            // nothing)...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 902, stateChangedAt: Now.UtcDateTime.AddMinutes(-1));
            harness.AdminLookup.Add(902, AdSpotWorkerHarness.MakeMediaRow(902, eligible: true), harness.AdsLibraryId);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the repair sweep never writes at all — the current-value guard, not merely
            // recency, is what keeps this the common no-op case.
            Assert.Empty(harness.CatalogWriter.SetEligibleHistory);
        }

        [Fact]
        public async Task AnOldReadySpotsIneligibleMediaRowIsLeftAlone()
        {
            // Given a ready spot whose OWN ready transition happened long ago (well outside the
            // guardian grace) and whose media row is ineligible — an OPERATOR's own hand
            // (never_play), never a race the repair sweep should second-guess (PLAN T402 review F1's
            // own pin: "the repair does NOT flip an old operator-disabled row")...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 901, stateChangedAt: Now.UtcDateTime.AddDays(-1));
            harness.AdminLookup.Add(901, AdSpotWorkerHarness.MakeMediaRow(901, eligible: false), harness.AdsLibraryId);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the repair sweep never touches it — operator intent stands.
            Assert.DoesNotContain((901L, true), harness.CatalogWriter.SetEligibleHistory);
        }
    }

    public sealed class ScenarioTickOrder
    {
        [Fact]
        public async Task AnAutoApprovedGenerationRendersInTheSameTick()
        {
            // Given AutoApprove=true, one enabled brief, and a render pipeline that will succeed (PLAN
            // T402 review F2(3) — kills a reorder that runs render BEFORE refill, which would leave a
            // same-tick auto-approved spot waiting an entire extra tick before it ever renders)...
            var harness = AdSpotWorkerHarness.Build(
                Now, Settings(autoApprove: true), llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(WellFormedReply));
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the worker ticks ONCE...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the SAME tick both generated AND rendered it — ready, not merely approved.
            var spot = Assert.Single(harness.Store.Spots);
            Assert.Equal(AdState.Ready, spot.State);
            Assert.Equal(1, harness.Store.MarkReadyCallCount);
        }
    }
}
