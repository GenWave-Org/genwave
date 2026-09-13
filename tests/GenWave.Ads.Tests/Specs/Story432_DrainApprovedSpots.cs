// STORY-432 — Every approved spot renders on the next tick (gh-#745 · PLAN T456)
//
// Runner: xUnit — the worker is a BackgroundService in GenWave.Ads, so the feature under spec is C#.
// Every fact drives the REAL AdSpotWorker through AdSpotWorkerHarness (fakes at the I/O edges only,
// the Story391_AdSpotWorker.cs precedent) via its one production tick, TickOnceAsync. RED at plan
// time: RenderOneIfDueAsync claims exactly one spot per tick today (the bug gh-#745 names), and
// Story391's ScenarioOneSpotPerTick pins that — T456 retires that fact when it turns these green.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using GenWave.Tts;

public static class FeatureEveryApprovedSpotRendersOnTheNextTick
{
    static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>PLAN T456's own per-tick ceiling (a constant on the worker, not a knob) — spelled out
    /// here rather than read off the worker so this file compiles before the constant exists.</summary>
    const int MaxRendersPerTick = 25;

    static void AddApproved(AdSpotWorkerHarness.Harness harness, int count)
    {
        for (var i = 1; i <= count; i++)
            harness.Store.AddSpot(i, AdState.Approved, stateChangedAt: Now.UtcDateTime.AddMinutes(-count + i - 1));
    }

    static Task Tick(AdSpotWorkerHarness.Harness harness) =>
        harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTwoApprovedSpotsOneTick : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            AddApproved(harness, 2);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOlderSpotIsReady()
            => Assert.Equal(AdState.Ready, harness.Store.Spots.Single(s => s.Id == 1).State);

        [Fact]
        public void TheNewerSpotIsReadyToo()
            => Assert.Equal(AdState.Ready, harness.Store.Spots.Single(s => s.Id == 2).State);

        [Fact]
        public void TwoRendersLanded()
            => Assert.Equal(2, harness.Store.MarkReadyCallCount);
    }

    public sealed class ScenarioTheClaimRunsUntilTheQueueIsEmpty : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            AddApproved(harness, 3);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThreeClaimsPlusTheEmptyOne()
            => Assert.Equal(4, harness.Store.ClaimCallCount);

        [Fact]
        public void NothingIsLeftApproved()
            => Assert.DoesNotContain(harness.Store.Spots, s => s.State == AdState.Approved);
    }

    public sealed class ScenarioTheDrainIsBounded : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            AddApproved(harness, MaxRendersPerTick + 5);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ExactlyTheCapRendered()
            => Assert.Equal(MaxRendersPerTick, harness.Store.Spots.Count(s => s.State == AdState.Ready));

        [Fact]
        public void TheRestWaitForTheNextTick()
            => Assert.Equal(5, harness.Store.Spots.Count(s => s.State == AdState.Approved));
    }

    public sealed class ScenarioAnEmptyQueueIsOneClaim : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public Task InitializeAsync() => Tick(harness);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheStoreSawExactlyOneClaim()
            => Assert.Equal(1, harness.Store.ClaimCallCount);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioARenderFailureDoesNotStopTheDrain : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            // Canned failure, no confirm (the Story391_AdRenderService precedent) — otherwise the fake's real confirmAsync lands MarkReady first.
            harness.Author.InvokeDelegates = false;
            harness.Author.Result = CastSegmentAuthorResult.Failure(
                CastSegmentFailureReason.ConfirmationFailed, "confirmation declined");
            AddApproved(harness, 2);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void BothSpotsWereClaimed()
            => Assert.Equal(3, harness.Store.ClaimCallCount);

        [Fact]
        public void BothSpotsAreFailed()
            => Assert.Equal(2, harness.Store.Spots.Count(s => s.State == AdState.Failed));
    }

    public sealed class ScenarioTheDrainYieldsToAnInFlightOnAirRender : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            AddApproved(harness, 2);
            harness.Gate.InFlight = true;
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoSpotWasClaimed()
            => Assert.Equal(0, harness.Store.ClaimCallCount);
    }
}
