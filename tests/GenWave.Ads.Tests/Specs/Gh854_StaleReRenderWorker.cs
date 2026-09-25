// gh-#854 — the worker's own background re-render pass (AdSpotWorker.ReRenderOneStaleAsync), which
// swaps a stale Ready spot onto freshly rendered media through IAdSpotStore.SwapRenderedMediaAsync's
// own single guarded transaction — nothing left for the worker itself to reconcile once that call
// returns. Every fact drives the REAL AdSpotWorker through AdSpotWorkerHarness (fakes at the I/O edges
// only, the Story391_AdSpotWorker.cs / Story432_DrainApprovedSpots.cs precedent) via its one production
// tick, TickOnceAsync.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using GenWave.Tts;

public static class Gh854FeatureTheWorkerReRendersStaleReadySpotsInTheBackground
{
    static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    static Task Tick(AdSpotWorkerHarness.Harness harness, CancellationToken ct = default) =>
        harness.Worker.TickOnceAsync(ct).WaitAsync(TimeSpan.FromSeconds(10));

    static void AddApproved(AdSpotWorkerHarness.Harness harness, int count)
    {
        for (var i = 1; i <= count; i++)
            harness.Store.AddSpot(1000 + i, AdState.Approved, stateChangedAt: Now.UtcDateTime.AddMinutes(-count + i - 1));
    }

    /// <summary>Registers an eligible, non-never_play admin-media row for <paramref name="mediaId"/> —
    /// every scenario meaning to reach a genuine re-render attempt needs this, since
    /// <see cref="AdSpotWorker.ReRenderOneStaleAsync"/>'s own cheap upfront check (gh-#854) declines
    /// before ever rendering when it is missing. The SAME facts are re-checked fresh, atomically, by
    /// <c>AdSpotRepository.SwapRenderedMediaAsync</c>'s own guard immediately before the swap itself —
    /// only that second check is honest against a fact changing mid-render.</summary>
    static void SeedEligibleOldMedia(AdSpotWorkerHarness.Harness harness, long mediaId) =>
        harness.AdminLookup.Add(mediaId, AdSpotWorkerHarness.MakeMediaRow(mediaId, eligible: true), harness.AdsLibraryId);

    // ---------------------------------------------------------------------
    // HAPPY PATH — a stale ready spot re-renders in the background
    // ---------------------------------------------------------------------

    public sealed class ScenarioAStaleReadySpotIsReRendered : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheMediaIdChanges()
            => Assert.NotEqual(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);

        [Fact]
        public void TheVersionIsStampedToCurrent()
            => Assert.Equal(AdRenderVersion.Current, harness.Store.Spots.Single(s => s.Id == 1).RenderVersion);

        [Fact]
        public void TheSpotStaysReady()
            => Assert.Equal(AdState.Ready, harness.Store.Spots.Single(s => s.Id == 1).State);

        [Fact]
        public void NeverCallsMarkReady()
            => Assert.Equal(0, harness.Store.MarkReadyCallCount);
    }

    /// <summary>gh-#854 — the two catalog flips <c>AdSpotRepository.SwapRenderedMediaAsync</c> issues
    /// after its own guarded commit run old-ineligible FIRST, then new-eligible SECOND, deliberately:
    /// the OTHER order's failure mode — a new-eligible flip landing before any old-ineligible flip is
    /// even attempted — would recreate the exact unreferenced-and-still-eligible orphan this whole
    /// redesign exists to close. Old-first avoids that shape entirely, and neither flip's own failure is
    /// a permanent leak either way: the SAME guarded transaction that committed the swap already
    /// stamped a durable marker for each media id (<c>pending_retire_media_id</c>,
    /// <c>pending_confirm_media_id</c>) before either flip ever ran, so a thrown or declined flip simply
    /// leaves its own marker for <c>AdSpotWorker</c>'s own drains (<c>DrainPendingRetiresAsync</c>,
    /// <c>DrainPendingConfirmsAsync</c>) to retry on a later tick.</summary>
    public sealed class ScenarioTheOldMediaGoesIneligibleBeforeTheNewOne : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOldMediaBecameIneligibleFirst()
            => Assert.Equal((500L, false), harness.CatalogWriter.SetEligibleHistory[0]);

        [Fact]
        public void TheNewMediaBecameEligibleLast()
            => Assert.Equal((4200L, true), harness.CatalogWriter.SetEligibleHistory[^1]);
    }

    public sealed class ScenarioApprovedSpotsRenderBeforeAStaleOne : IAsyncLifetime
    {
        readonly CapturingLogger<AdSpotWorker> logger = new();
        readonly AdSpotWorkerHarness.Harness harness;
        int approvedIndex;
        int staleIndex;

        public ScenarioApprovedSpotsRenderBeforeAStaleOne()
        {
            harness = AdSpotWorkerHarness.Build(Now, workerLogger: logger);
        }

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Approved, stateChangedAt: Now.UtcDateTime);
            harness.Store.AddSpot(2, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            await Tick(harness);

            approvedIndex = logger.Messages.FindIndex(m => m.Contains("rendered 1 approved spot(s) this tick", StringComparison.Ordinal));
            staleIndex = logger.Messages.FindIndex(m => m.Contains("stale re-render swapped off media", StringComparison.Ordinal));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheApprovedDrainLineIsLogged()
            => Assert.True(approvedIndex >= 0);

        [Fact]
        public void TheStaleReRenderLineIsLogged()
            => Assert.True(staleIndex >= 0);

        [Fact]
        public void TheApprovedLineComesBeforeTheStaleLine()
            => Assert.True(approvedIndex < staleIndex);
    }

    public sealed class ScenarioAtMostOneReRenderPerTick : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-2), renderVersion: 0);
            harness.Store.AddSpot(2, AdState.Ready, mediaId: 501, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            SeedEligibleOldMedia(harness, 501);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ExactlyOneSpotSwapped()
            => Assert.Equal(1, harness.Store.SwapRenderedMediaCallCount);
    }

    public sealed class ScenarioACurrentVersionSpotIsLeftAlone : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: AdRenderVersion.Current);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoSwapIsAttempted()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);
    }

    // ---------------------------------------------------------------------
    // gh-#854 — an operator-disabled or never_play old row is never re-rendered
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheOldMediaIsOperatorDisabled : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            harness.AdminLookup.Add(500, AdSpotWorkerHarness.MakeMediaRow(500, eligible: false), harness.AdsLibraryId);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotIsNeverReRendered()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);
    }

    public sealed class ScenarioTheOldMediaIsNeverPlay : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            harness.AdminLookup.Add(
                500, AdSpotWorkerHarness.MakeMediaRow(500, eligible: true) with { NeverPlay = true }, harness.AdsLibraryId);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotIsNeverReRendered()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);
    }

    /// <summary>gh-#854 — the old media's eligibility is checked once, upfront, before a render even
    /// starts (a render that may run for seconds) AND once more, fresh, immediately before the swap.
    /// Only the second check is honest against an operator disabling the row mid-render — this spec
    /// proves it, not the upfront one above.</summary>
    public sealed class ScenarioTheOldMediaGoesOperatorDisabledDuringTheRender : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            harness.Author.BlockBeforeConfirm = true;

            var tickTask = Tick(harness);

            // The render produced its media and is about to confirm — the old row is still eligible
            // right now, exactly as it was when this tick's upfront check passed.
            await harness.Author.EnteredBeforeConfirm.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The operator disables the old row in exactly this window.
            harness.AdminLookup.Add(500, AdSpotWorkerHarness.MakeMediaRow(500, eligible: false), harness.AdsLibraryId);
            harness.Author.ReleaseBeforeConfirm.TrySetResult();

            await tickTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoSwapIsAttempted()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);

        [Fact]
        public void TheNewMediaIsNeverConfirmedEligible()
            => Assert.Empty(harness.CatalogWriter.SetEligibleHistory);

        [Fact]
        public void TheSpotStaysOnItsOldMedia()
            => Assert.Equal(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);
    }

    // ---------------------------------------------------------------------
    // gh-#854 — a host shutdown mid-render, and a swap that never commits, both leave the OLD row
    // exactly as it was: still eligible, still what the spot's own media id names. The new row is
    // never confirmed eligible in either case — IAdSpotStore.SwapRenderedMediaAsync is the only place
    // that flip can happen, and it never runs unless its own guarded UPDATE actually affected a row.
    // ---------------------------------------------------------------------

    /// <summary>gh-#854 — a host shutdown landing while the render is still in flight, before
    /// <c>confirmAsync</c> (<c>SwapRenderedMediaAsync</c>) is ever called, must leave the spot exactly
    /// where it started: on its old media, still Ready, with no eligibility flip ever attempted. The
    /// mechanics mirror <see cref="ScenarioABudgetCancellationMidReRenderLeavesTheSpotUntouched"/> below,
    /// but through <c>stoppingToken</c> itself — the token <c>TickOnceAsync</c> runs on, never the
    /// render's own bounded budget — proving the same guarantee holds for a genuine shutdown, not only
    /// a slow render.</summary>
    public sealed class ScenarioAHostShutdownDuringTheRenderLeavesTheSpotUntouched : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);
        readonly CancellationTokenSource stoppingCts = new();

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            harness.Author.BlockUntilCancelled = true;

            var tickTask = Tick(harness, stoppingCts.Token);

            await harness.Author.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The host shuts down while the render is still in flight — well before any swap attempt.
            await stoppingCts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tickTask);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotStaysOnItsOldMedia()
            => Assert.Equal(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);

        [Fact]
        public void TheSpotStaysReady()
            => Assert.Equal(AdState.Ready, harness.Store.Spots.Single(s => s.Id == 1).State);

        [Fact]
        public void NoSwapWasEverAttempted()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);

        [Fact]
        public void NoMediaEligibilityIsEverTouched()
            => Assert.Empty(harness.CatalogWriter.SetEligibleHistory);
    }

    /// <summary>gh-#854 — the new media row is never confirmed eligible unless the guarded swap itself
    /// commits: an operator retiring the spot in the window between the render finishing and
    /// <c>SwapRenderedMediaAsync</c>'s own state-guarded <c>UPDATE</c> leaves that call a no-op — the
    /// spot stays Retired, on its OLD media id, and BOTH catalog flips (which only ever run after a
    /// commit) never happen. No compensation, no second attempt: the freshly rendered (never-eligible)
    /// media row is simply left behind, inert.</summary>
    public sealed class ScenarioTheSpotIsRetiredBeforeTheSwapCommits : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            harness.Author.BlockBeforeConfirm = true;

            var tickTask = Tick(harness);

            // The render produced its media and is about to confirm — before SwapRenderedMediaAsync's
            // own state guard ever runs.
            await harness.Author.EnteredBeforeConfirm.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The operator retires the spot in exactly this window.
            var current = harness.Store.Spots.Single(s => s.Id == 1);
            var retired = await harness.Store.RetireAsync(1, current.Version, CancellationToken.None);
            if (retired.Result != AdSpotWriteResult.Updated)
                throw new InvalidOperationException("arrange: retiring the spot mid-render did not apply");

            harness.Author.ReleaseBeforeConfirm.TrySetResult();

            await tickTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotStaysRetiredOnItsOldMedia()
        {
            var spot = harness.Store.Spots.Single(s => s.Id == 1);
            Assert.Equal(AdState.Retired, spot.State);
            Assert.Equal(500L, spot.MediaId);
        }

        [Fact]
        public void NoMediaEligibilityIsEverTouched()
            => Assert.Empty(harness.CatalogWriter.SetEligibleHistory);
    }

    // ---------------------------------------------------------------------
    // gh-#854 — the stale pass never even starts under these conditions
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnInFlightRenderSignalSkipsTheStalePass : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            // gh-#854 — flips InFlight from inside the approved drain's own empty-queue return, so
            // this spec fails if ReRenderOneStaleAsync's own gate re-check were ever deleted: the ONLY
            // thing stopping the stale pass here is that guard, never a queue that was already empty
            // when the tick began.
            harness.Store.OnApprovedQueueObservedEmpty = () => harness.Gate.InFlight = true;
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheStaleSpotIsNeverSwapped()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);
    }

    public sealed class ScenarioThePerTickCapLeavesNoRoomForTheStalePass : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            AddApproved(harness, AdSpotWorker.MaxRendersPerTick + 5);
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheStaleSpotIsNeverSwapped()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);
    }

    public sealed class ScenarioABudgetCancellationMidReRenderLeavesTheSpotUntouched : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now, renderBudgetSeconds: 1);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            harness.Author.BlockUntilCancelled = true;

            var tickTask = Tick(harness);

            await harness.Author.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            harness.TimeProvider.Advance(TimeSpan.FromSeconds(2)); // past the 1s render budget above

            await tickTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotStaysOnItsOldMedia()
            => Assert.Equal(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);

        [Fact]
        public void NoSwapWasEverAttempted()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);
    }

    /// <summary>gh-#854 — a budget-exceeded stale re-render joins the process-lifetime skip set (the
    /// SAME set a genuine failure joins), so one always-slow spot can never starve every stale spot
    /// behind it. A break-window yield stays retryable — never added to this set.</summary>
    public sealed class ScenarioABudgetTimeoutOnTheOldestSpotStillLetsTheNextOneSwap : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now, renderBudgetSeconds: 1);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-2), renderVersion: 0);
            harness.Store.AddSpot(2, AdState.Ready, mediaId: 501, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            SeedEligibleOldMedia(harness, 501);
            harness.Author.BlockUntilCancelled = true;

            var tickTask = Tick(harness);

            // Spot 1 (the older of the two) times out against its own budget.
            await harness.Author.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            harness.TimeProvider.Advance(TimeSpan.FromSeconds(2)); // past the 1s render budget above

            await tickTask;

            // A second tick: spot 1 is now in the process-lifetime skip set, so this tick reaches spot 2.
            harness.Author.BlockUntilCancelled = false;
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOlderSpotStaysOnItsOldMedia()
            => Assert.Equal(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);

        [Fact]
        public void TheYoungerSpotSwaps()
            => Assert.NotEqual(501L, harness.Store.Spots.Single(s => s.Id == 2).MediaId);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated) — a stale re-render failure never disturbs the airing spot
    // ---------------------------------------------------------------------

    public sealed class ScenarioARenderFailureLeavesTheSpotUntouched : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Author.InvokeDelegates = false;
            harness.Author.Result = CastSegmentAuthorResult.Failure(CastSegmentFailureReason.ConfirmationFailed, "confirmation declined");
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotStaysOnItsOldMedia()
            => Assert.Equal(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);

        [Fact]
        public void TheSpotStaysReady()
            => Assert.Equal(AdState.Ready, harness.Store.Spots.Single(s => s.Id == 1).State);

        [Fact]
        public void TheVersionIsUnchanged()
            => Assert.Equal(0, harness.Store.Spots.Single(s => s.Id == 1).RenderVersion);

        [Fact]
        public void NoMediaEligibilityIsEverTouched()
            => Assert.Equal(0, harness.CatalogWriter.SetEligibleCalls);
    }

    public sealed class ScenarioAFailedReRenderIsNotRetriedWithinTheSameProcess : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            harness.Author.InvokeDelegates = false;
            harness.Author.Result = CastSegmentAuthorResult.Failure(CastSegmentFailureReason.ConfirmationFailed, "confirmation declined");
            await Tick(harness); // first tick: fails, the spot joins the process-lifetime skip set.

            // Flip the author back to a genuine success — if the worker retried, this tick would swap.
            harness.Author.InvokeDelegates = true;
            harness.Author.Result = CastSegmentAuthorResult.Success(4200);
            await Tick(harness); // second tick: same process — must still skip spot 1.
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotIsNeverSwappedThisProcess()
            => Assert.Equal(0, harness.Store.SwapRenderedMediaCallCount);
    }
}
