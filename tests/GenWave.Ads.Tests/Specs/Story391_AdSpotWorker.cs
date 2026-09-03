// STORY-391 — Spots render OFF the clock (worker half: AC4 · F161.1 · PLAN T402)
// AC6 (the stuck-rendering guardian) is specced with the stock pass in Story389_AdStockKeeping.cs.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

public static class FeatureAdSpotWorker
{
    static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    const string WellFormedReply = "ANNOUNCER: Come on down to the big sale.\nVOICE1: Prices you won't believe.";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOffTheAirClock
    {
        [Fact]
        public async Task NoRenderStartsWhileABreakWindowIsOpen()
        {
            // Given an approved spot and an on-air render already in flight...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved);
            harness.Gate.InFlight = true;

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the spot was never even claimed — no render started.
            Assert.Equal(0, harness.Store.ClaimCallCount);
        }

        [Fact]
        public async Task AnInFlightRenderIsCanceledWhenTheWindowOpens()
        {
            // Given an approved spot whose render genuinely starts (the gate is closed at first —
            // the positive-control half of the pair with the fact above) and blocks mid-flight...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved);
            harness.Author.BlockUntilCancelled = true;

            var tickTask = harness.Worker.TickOnceAsync(CancellationToken.None);

            // Then the render genuinely started — the spot was claimed and the author is now blocked
            // in flight (the positive-control half of the pair).
            await harness.Author.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(AdState.Rendering, Assert.Single(harness.Store.Spots).State);

            // When a real on-air render starts mid-flight and the watchdog's next poll observes it...
            harness.Gate.InFlight = true;
            harness.TimeProvider.Advance(TimeSpan.FromSeconds(3)); // AdSpotWorker's own RenderWatchdogInterval

            // Then the in-flight render is genuinely cancelled — the tick completes (never hangs) and
            // the author's own token was truly cancelled, not merely abandoned.
            await tickTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(harness.Author.WasCancelled);
        }

        [Fact]
        public async Task RenderingResumesAfterTheWindowCloses()
        {
            // Given a render that yielded to an open break window (the SAME shape as the fact above,
            // driven to completion so the spot re-arms)...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved);
            harness.Author.BlockUntilCancelled = true;

            var firstTick = harness.Worker.TickOnceAsync(CancellationToken.None);
            await harness.Author.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            harness.Gate.InFlight = true;
            harness.TimeProvider.Advance(TimeSpan.FromSeconds(3));
            await firstTick.WaitAsync(TimeSpan.FromSeconds(5));

            // When the break window closes and a LATER tick runs — this time the render is allowed to
            // complete for real...
            harness.Gate.InFlight = false;
            harness.Author.BlockUntilCancelled = false;

            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the SAME spot resumed and rendered through to ready — no operator intervention, no
            // second spot, no orphaned row left behind in rendering.
            var spot = Assert.Single(harness.Store.Spots);
            Assert.Equal(AdState.Ready, spot.State);
            Assert.Equal(1, harness.Store.MarkReadyCallCount);
        }
    }

    public sealed class ScenarioOneSpotPerTick
    {
        [Fact]
        public async Task TwoApprovedSpotsTakeTwoTicks()
        {
            // Given two approved spots...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved, stateChangedAt: Now.UtcDateTime.AddMinutes(-2));
            harness.Store.AddSpot(2, AdState.Approved, stateChangedAt: Now.UtcDateTime.AddMinutes(-1));

            // When the worker ticks once...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then exactly one rendered — the older one, oldest-first — the second is untouched.
            Assert.Equal(AdState.Ready, harness.Store.Spots.Single(s => s.Id == 1).State);
            Assert.Equal(AdState.Approved, harness.Store.Spots.Single(s => s.Id == 2).State);

            // When a second tick runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the second spot renders too — two ticks, two renders, never both in one.
            Assert.Equal(AdState.Ready, harness.Store.Spots.Single(s => s.Id == 2).State);
            Assert.Equal(2, harness.Store.MarkReadyCallCount);
        }
    }
}
