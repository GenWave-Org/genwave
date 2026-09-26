// gh-#854 — db/48's own pending_retire_media_id column, and the worker-tick drain that makes the
// old-media turn-off half of IAdSpotStore.SwapRenderedMediaAsync durable across a crash or a failed
// flip: every old media row a swap displaces is turned off eventually, never an accepted, silent race.
// Every fact drives the REAL AdSpotWorker through AdSpotWorkerHarness (fakes at the I/O edges only, the
// Story391_AdSpotWorker.cs / Story432_DrainApprovedSpots.cs precedent) via its one production tick,
// TickOnceAsync.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

public static class Gh854FeatureThePendingRetireMarkerSurvivesAFailedFlip
{
    static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    static Task Tick(AdSpotWorkerHarness.Harness harness, CancellationToken ct = default) =>
        harness.Worker.TickOnceAsync(ct).WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>Registers an eligible, non-never_play admin-media row for <paramref name="mediaId"/> —
    /// the same upfront-guard fact <see cref="Gh854FeatureTheWorkerReRendersStaleReadySpotsInTheBackground"/>'s
    /// own <c>SeedEligibleOldMedia</c> registers.</summary>
    static void SeedEligibleOldMedia(AdSpotWorkerHarness.Harness harness, long mediaId) =>
        harness.AdminLookup.Add(mediaId, AdSpotWorkerHarness.MakeMediaRow(mediaId, eligible: true), harness.AdsLibraryId);

    /// <summary>gh-#854 — a thrown old-media flip must never lose the fact that the swap already
    /// committed: the pending marker survives it, and a later tick (once the flip stops throwing —
    /// e.g. a transient library outage clears) finishes the job and clears it. This is the whole point
    /// of db/48's own <c>pending_retire_media_id</c> column: the fact survives a failed flip, or a
    /// crash between the commit and the flip, in the DB itself, not just in this process's memory.</summary>
    public sealed class ScenarioAFlipThatThrowsIsRetriedAndClearedOnTheNextTick : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            harness.CatalogWriter.ThrowOnSetEligible.Add(500);
            await Tick(harness); // the swap commits; the old-media flip throws and stays pending.

            harness.CatalogWriter.ThrowOnSetEligible.Remove(500);
            await Tick(harness); // no new stale work this tick — only the drain has anything to do.
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapAlreadyCommittedOnTheFirstTick()
            => Assert.NotEqual(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);

        [Fact]
        public void TheOldMediaIsEventuallyFlippedIneligible()
            => Assert.Contains((500L, false), harness.CatalogWriter.SetEligibleHistory);

        [Fact]
        public async Task ThePendingMarkerIsClearedAfterTheRetrySucceeds()
            => Assert.Empty(await harness.Store.ListPendingRetiresAsync(CancellationToken.None));
    }

    /// <summary>gh-#854 — an operator re-pointing another spot at the very media a pending retire was
    /// about to turn off must never cost that OTHER spot its own on-air media: the marker simply
    /// clears, no flip ever attempted, exactly db/48's own guard UPDATE this drain mirrors in
    /// memory.</summary>
    public sealed class ScenarioAPendingMediaReReferencedByAnotherSpotClearsWithoutFlipping : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            // The old-media flip throws, so the swap's own pending marker survives past the first tick.
            harness.CatalogWriter.ThrowOnSetEligible.Add(500);
            await Tick(harness);

            // An operator re-points a second, unrelated spot at the very media the pending retire was
            // about to turn off — that spot's own media_id is now the authority, not the pending marker.
            harness.Store.AddSpot(2, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime);

            harness.CatalogWriter.ThrowOnSetEligible.Remove(500);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ThePendingMarkerIsCleared()
            => Assert.Empty(await harness.Store.ListPendingRetiresAsync(CancellationToken.None));

        [Fact]
        public void TheReReferencedMediaIsNeverFlippedIneligible()
            => Assert.DoesNotContain((500L, false), harness.CatalogWriter.SetEligibleHistory);
    }

    /// <summary>gh-#854 — the common case: nothing ever throws, so the old-media flip lands in the
    /// SAME tick the swap itself commits, and the pending marker never survives past that one call.
    /// The drain at the top of the NEXT tick has nothing left to do.</summary>
    public sealed class ScenarioTheHappyPathClearsThePendingMarkerInTheSameTick : IAsyncLifetime
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
        public void TheOldMediaIsFlippedIneligible()
            => Assert.Contains((500L, false), harness.CatalogWriter.SetEligibleHistory);

        [Fact]
        public async Task ThePendingMarkerIsAlreadyClear()
            => Assert.Empty(await harness.Store.ListPendingRetiresAsync(CancellationToken.None));
    }
}

/// <summary>gh-#854 — <see cref="Gh854FeatureThePendingRetireMarkerSurvivesAFailedFlip"/>'s own facts,
/// one marker over: <c>pending_confirm_media_id</c> and the worker's own
/// <c>DrainPendingConfirmsAsync</c>, which makes the NEW media's own eligibility flip durable across a
/// crash or a failed flip in exactly the same way.</summary>
public static class Gh854FeatureThePendingConfirmMarkerSurvivesAFailedFlip
{
    static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The media id <c>FakeCastSegmentAuthor</c>'s own default render always lands on — the
    /// same fact <see cref="Gh854FeatureTheWorkerReRendersStaleReadySpotsInTheBackground.ScenarioTheOldMediaGoesIneligibleBeforeTheNewOne"/>
    /// already asserts against.</summary>
    const long NewMediaId = 4200;

    static Task Tick(AdSpotWorkerHarness.Harness harness, CancellationToken ct = default) =>
        harness.Worker.TickOnceAsync(ct).WaitAsync(TimeSpan.FromSeconds(10));

    static void SeedEligibleOldMedia(AdSpotWorkerHarness.Harness harness, long mediaId) =>
        harness.AdminLookup.Add(mediaId, AdSpotWorkerHarness.MakeMediaRow(mediaId, eligible: true), harness.AdsLibraryId);

    /// <summary>gh-#854 — a thrown new-media flip must never lose the fact that the swap already
    /// committed: this is the confirm-marker twin of
    /// <see cref="Gh854FeatureThePendingRetireMarkerSurvivesAFailedFlip.ScenarioAFlipThatThrowsIsRetriedAndClearedOnTheNextTick"/>.
    /// The general repair sweep (<c>RepairReadyEligibilityAsync</c>) only ever looks at rows past its own
    /// age threshold — this spot never ages into that window inside the test, so the healing here can
    /// only be <c>DrainPendingConfirmsAsync</c>'s own retry, never that unrelated sweep.</summary>
    public sealed class ScenarioAConfirmFlipThatThrowsIsRetriedAndClearedOnTheNextTick : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            harness.CatalogWriter.ThrowOnSetEligible.Add(NewMediaId);
            await Tick(harness); // the swap commits; the old-media flip lands; the new-media flip throws and stays pending.

            harness.CatalogWriter.ThrowOnSetEligible.Remove(NewMediaId);
            await Tick(harness); // no new stale work this tick — only the confirm drain has anything to do.
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapAlreadyCommittedOnTheFirstTick()
            => Assert.NotEqual(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);

        [Fact]
        public void TheNewMediaIsEventuallyFlippedEligible()
            => Assert.Contains((NewMediaId, true), harness.CatalogWriter.SetEligibleHistory);

        [Fact]
        public async Task ThePendingMarkerIsClearedAfterTheRetrySucceeds()
            => Assert.Empty(await harness.Store.ListPendingConfirmsAsync(CancellationToken.None));
    }

    /// <summary>gh-#854 — an operator retiring the spot in the window between a swap's own commit and
    /// the confirm drain's later retry must never revive media the operator meant to pull off air: the
    /// drain's own <c>IsReadyOnMediaAsync</c> guard declines the flip entirely (never calls
    /// <c>SetEligibleAsync</c> at all), and simply clears the now-moot marker instead of leaving it
    /// pending forever.</summary>
    public sealed class ScenarioAConfirmDrainSkipsAFlipForASpotRetiredInBetween : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            harness.CatalogWriter.ThrowOnSetEligible.Add(NewMediaId);
            await Tick(harness); // the swap commits; the new-media flip throws and stays pending.
            harness.CatalogWriter.ThrowOnSetEligible.Remove(NewMediaId);

            // The operator retires the spot before the drain ever gets its own retry.
            var current = harness.Store.Spots.Single(s => s.Id == 1);
            var retired = await harness.Store.RetireAsync(1, current.Version, CancellationToken.None);
            if (retired.Result != AdSpotWriteResult.Updated)
                throw new InvalidOperationException("arrange: retiring the spot did not apply");

            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNewMediaIsNeverFlippedEligible()
            => Assert.DoesNotContain((NewMediaId, true), harness.CatalogWriter.SetEligibleHistory);

        [Fact]
        public async Task ThePendingMarkerIsClearedAnyway()
            => Assert.Empty(await harness.Store.ListPendingConfirmsAsync(CancellationToken.None));
    }
}

/// <summary>gh-#854 — <see cref="IAuthoredCatalogWriter.SetEligibleAsync"/> reporting
/// <see langword="false"/> means the row was already hard-deleted by <c>PurgeUnavailableAsync</c>, not a
/// transient failure: unlike a thrown flip, this clears the marker in the SAME call rather than leaving
/// it for a retry that could never succeed against a row that no longer exists.</summary>
public static class Gh854FeatureAPurgedMediaRowClearsItsMarkerWithoutARetry
{
    static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    const long NewMediaId = 4200;

    static Task Tick(AdSpotWorkerHarness.Harness harness, CancellationToken ct = default) =>
        harness.Worker.TickOnceAsync(ct).WaitAsync(TimeSpan.FromSeconds(10));

    static void SeedEligibleOldMedia(AdSpotWorkerHarness.Harness harness, long mediaId) =>
        harness.AdminLookup.Add(mediaId, AdSpotWorkerHarness.MakeMediaRow(mediaId, eligible: true), harness.AdsLibraryId);

    public sealed class ScenarioTheOldMediaWasAlreadyPurged : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            harness.CatalogWriter.DeclineOnSetEligible.Add(500);
            await Tick(harness); // the swap commits; the old media's own row was already purged — SetEligibleAsync reports false, not a throw.
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ThePendingRetireMarkerIsClearedRatherThanLeftForARetry()
            => Assert.Empty(await harness.Store.ListPendingRetiresAsync(CancellationToken.None));
    }

    public sealed class ScenarioTheNewMediaWasAlreadyPurged : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            harness.CatalogWriter.DeclineOnSetEligible.Add(NewMediaId);
            await Tick(harness); // the swap commits; the freshly rendered row was already purged before the confirm flip ran.
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ThePendingConfirmMarkerIsClearedRatherThanLeftForARetry()
            => Assert.Empty(await harness.Store.ListPendingConfirmsAsync(CancellationToken.None));
    }
}

/// <summary>gh-#854 — a spot still carrying either pending marker from an earlier swap is not a
/// fresh candidate for a second one, even on a build that later bumps <c>AdRenderVersion.Current</c>
/// again while that marker is still outstanding: <c>FindStaleReadyAsync</c> excludes it regardless of its
/// own render version, so a second swap can never overwrite the first one's still-pending marker.</summary>
public static class Gh854FeatureAPendingMarkerExcludesASecondStalePass
{
    static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    static void SeedEligibleOldMedia(AdSpotWorkerHarness.Harness harness, long mediaId) =>
        harness.AdminLookup.Add(mediaId, AdSpotWorkerHarness.MakeMediaRow(mediaId, eligible: true), harness.AdsLibraryId);

    public sealed class ScenarioASpotWithAPendingMarkerIsExcludedEvenWhenItsOwnRenderVersionIsStillStale : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            SeedEligibleOldMedia(harness, 500);

            // Stamps a pending marker through the store's own guarded swap — the exact call
            // AdSpotWorker.ReRenderOneStaleAsync makes — but deliberately landing the spot back on a
            // still-stale render version (0), so FindStaleReadyAsync's own version filter alone would
            // otherwise select it again for a second swap.
            harness.CatalogWriter.ThrowOnSetEligible.Add(500);
            var swapped = await harness.Store.SwapRenderedMediaAsync(
                1, oldMediaId: 500, newMediaId: 4200, renderVersion: 0, CancellationToken.None);
            if (!swapped)
                throw new InvalidOperationException("arrange: the swap did not land");
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TheSpotIsExcludedFromASecondStalePassWhileItsMarkerIsPending()
        {
            var candidate = await harness.Store.FindStaleReadyAsync(AdRenderVersion.Current, [], CancellationToken.None);
            Assert.Null(candidate);
        }
    }
}
