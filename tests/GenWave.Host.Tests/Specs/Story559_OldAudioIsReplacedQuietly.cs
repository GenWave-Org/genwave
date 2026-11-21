// STORY-559 — Old audio is replaced quietly (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. The ad half of the sweep (one ad re-rendered per tick) lives in
// GenWave.Ads.Tests/Specs/Story559_OldAudioIsReplacedQuietly.cs beside the ad worker harness; the status
// count (T764) stays pending here.

namespace GenWave.Host.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;
using GenWave.Host.Seeding;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;

public static class FeatureOldAudioIsReplacedQuietly
{
    // AC3 — stale station-voice content from the seeder, after a switch
    public sealed class ScenarioOnePerIdleTick : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 2);

        public async Task InitializeAsync() => await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void SeederContentIsReRenderedOnePerTick() => Assert.Single(rig.Author.Requests);
    }

    public sealed class ScenarioTheNextTick : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 2);

        public async Task InitializeAsync()
        {
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOtherItemIsReRendered() => Assert.Equal(2, rig.Author.Requests.Count);

        [Fact]
        public void BothItemsNowCarryTheCurrentMarker() =>
            Assert.All(rig.Ledger.Records, record => Assert.Equal(rig.CurrentMarker, record.Marker));
    }

    // AC4 — while the replacement is still rendering
    public sealed class ScenarioTheOldFileAirsMeanwhile : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1, gated: true);
        Task tick = Task.CompletedTask;

        public async Task InitializeAsync()
        {
            tick = rig.Resweeper.TickOnceAsync(CancellationToken.None);
            await rig.Author.Started.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async Task DisposeAsync()
        {
            rig.Author.Release(SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, "test over"));
            await tick.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void AStaleItemIsStillEligible() => Assert.True(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));
    }

    public sealed class ScenarioTheReplacementIsIn : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync() => await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOldRowIsSwitchedOff() => Assert.False(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));

        [Fact]
        public void TheOldRowIsNotDeleted() => Assert.True(rig.Catalog.Exists(SweepRig.FirstOldMediaId));

        [Fact]
        public void TheLedgerNamesTheNewRow() =>
            Assert.Equal(SweepRig.FirstNewMediaId, rig.Ledger.Records.Single().MediaId);

        [Fact]
        public void TheNewRowIsRenderedInTheOldRowsLibrary() =>
            Assert.Equal(SweepRig.SafeLibraryId, rig.Author.Requests.Single().LibraryId);

        [Fact]
        public void TheNewRowIsRenderedWithTheStationVoice() =>
            Assert.Equal(SweepRig.Voice, rig.Author.Requests.Single().DefaultVoice);
    }

    // AC5 — pending T764
    public sealed class ScenarioTheCount
    {
        [Fact(Skip = "pending: T764")]
        public void TheStatusResponseCarriesFour() { }
    }

    // Sad path
    // AC6 — uploaded imaging
    public sealed class ScenarioDeansOwnAudio : IAsyncLifetime
    {
        // The ledger is empty (nothing recorded), so the sweep must look the seed row up by itself. An upload
        // that Dean titled exactly like the seed, but which lives outside the authored root, is not it.
        readonly SweepRig rig = SweepRig.WithUploadedImagingOnly();

        public async Task InitializeAsync() => await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSweepSkipsIt() => Assert.Empty(rig.Author.Requests);
    }

    // A station seeded before the sweep existed has no ledger entry; its seed row is adopted.
    public sealed class ScenarioAnEarlierSeedRow : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.WithEarlierSeedRow();

        public async Task InitializeAsync() => await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsReRenderedOnce() => Assert.Single(rig.Author.Requests);

        [Fact]
        public void TheLedgerNamesTheNewRow() =>
            Assert.Equal(SweepRig.FirstNewMediaId, rig.Ledger.Records.Single().MediaId);
    }

    public sealed class ScenarioNothingIsStale : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 0);

        public async Task InitializeAsync() => await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsRendered() => Assert.Empty(rig.Author.Requests);
    }

    public sealed class ScenarioABreakIsRendering : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync()
        {
            rig.Signal.InFlight = true;
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsRendered() => Assert.Empty(rig.Author.Requests);
    }

    public sealed class ScenarioTheStationHasNoVoiceForTheEngine : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync()
        {
            rig.Station.Voice = "{\"chatterbox\":\"nova\"}";
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsRendered() => Assert.Empty(rig.Author.Requests);

        [Fact]
        public void TheOldRowKeepsAiring() => Assert.True(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));
    }

    public sealed class ScenarioTheRenderFails : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync()
        {
            rig.Author.NextResult = SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, "engine down");
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOldRowKeepsAiring() => Assert.True(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));

        [Fact]
        public void TheLedgerStillNamesTheOldRow() =>
            Assert.Equal(SweepRig.FirstOldMediaId, rig.Ledger.Records.Single().MediaId);

        [Fact]
        public void ItIsNotTriedAgainOnTheNextTick() => Assert.Single(rig.Author.Requests);
    }

    public sealed class ScenarioTheHoldOffEnds : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync()
        {
            rig.Author.NextResult = SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, "engine down");
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            rig.Time.Advance(SafeContentResweeper.RetryAfter + TimeSpan.FromSeconds(1));
            rig.Author.NextResult = null;
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsTriedAgainAndReplaced() =>
            Assert.Equal(SweepRig.FirstNewMediaId, rig.Ledger.Records.Single().MediaId);
    }

    public sealed class ScenarioTheVoiceIsFixedAfterAFailure : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync()
        {
            rig.Author.NextResult = SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, "bad voice");
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            rig.Station.Voice = "af_sky";
            rig.Author.NextResult = null;
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsTriedAtOnceWithoutWaitingOutTheHoldOff() =>
            Assert.Equal(SweepRig.FirstNewMediaId, rig.Ledger.Records.Single().MediaId);
    }

    public sealed class ScenarioSwitchingTheOldRowOffFails : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync()
        {
            rig.Catalog.FailNextSwitchOff = true;
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNextTickFinishesSwitchingItOff() => Assert.False(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));

        [Fact]
        public void NothingIsRenderedTwice() => Assert.Single(rig.Author.Requests);

        [Fact]
        public void TheLedgerNoLongerWaitsOnTheOldRow() =>
            Assert.Null(rig.Ledger.Records.Single().PendingRetireMediaId);
    }

    public sealed class ScenarioABreakOpensDuringTheRender : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1, gated: true);

        public async Task InitializeAsync()
        {
            var tick = rig.Resweeper.TickOnceAsync(CancellationToken.None);
            await rig.Author.Started.WaitAsync(TimeSpan.FromSeconds(10));
            rig.Signal.InFlight = true;
            rig.Time.Advance(SafeContentResweeper.WatchdogInterval);
            await tick.WaitAsync(TimeSpan.FromSeconds(10));

            // The gate is open for the retry, so a second render finishes at once (and fails) instead of waiting forever.
            rig.Signal.InFlight = false;
            rig.Author.Release(SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, "test over"));
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRenderIsGivenUp() => Assert.False(rig.Catalog.Exists(SweepRig.FirstNewMediaId));

        [Fact]
        public void TheOldRowKeepsAiring() => Assert.True(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));

        [Fact]
        public void TheLedgerIsUnchanged() => Assert.Equal(SweepRig.FirstOldMediaId, rig.Ledger.Records.Single().MediaId);

        [Fact]
        public void ItIsTriedAgainOnTheNextTickWithNoHoldOff() => Assert.Equal(2, rig.Author.Requests.Count);
    }

    public sealed class ScenarioTheFirstItemWasSwitchedOff : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 2);

        public async Task InitializeAsync()
        {
            await rig.Catalog.SetEligibleAsync(SweepRig.FirstOldMediaId, false, CancellationToken.None);
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSecondItemIsReRenderedOnTheFirstTick() =>
            Assert.Contains(rig.Ledger.Records, r => r.MediaId == SweepRig.FirstNewMediaId);

        [Fact]
        public void TheSwitchedOffItemIsDroppedFromTheLedger() =>
            Assert.DoesNotContain(rig.Ledger.Records, r => r.MediaId == SweepRig.FirstOldMediaId);

        [Fact]
        public void NothingIsRenderedAgainOnTheNextTick() => Assert.Single(rig.Author.Requests);

        [Fact]
        public void TheSecondOldRowIsSwitchedOff() => Assert.False(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId + 1));
    }

    public sealed class ScenarioTheLedgerCannotBeSaved : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1);

        public async Task InitializeAsync()
        {
            rig.Ledger.FailSaves = true;
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheUntrackedNewRowDoesNotAir() => Assert.False(rig.Catalog.IsEligible(SweepRig.FirstNewMediaId));

        [Fact]
        public void TheOldRowKeepsAiring() => Assert.True(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));

        [Fact]
        public void NothingIsRenderedAgainBeforeTheHoldOffEnds() => Assert.Single(rig.Author.Requests);
    }

    public sealed class ScenarioTheRenderOutlastsTheEnginesBudget : IAsyncLifetime
    {
        static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
        readonly SweepRig rig = SweepRig.AfterSwitch(staleItems: 1, gated: true);

        public async Task InitializeAsync()
        {
            rig.RenderBudget.Budget = Budget;
            var tick = rig.Resweeper.TickOnceAsync(CancellationToken.None);
            await rig.Author.Started.WaitAsync(TimeSpan.FromSeconds(10));
            rig.Time.Advance(Budget);
            await tick.WaitAsync(TimeSpan.FromSeconds(10));

            // The gate is open for the next tick, so a second render would finish at once: it is not made while held back.
            rig.Author.Release(SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, "test over"));
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOldRowKeepsAiring() => Assert.True(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));

        [Fact]
        public void TheRenderIsHeldBack() => Assert.Single(rig.Author.Requests);
    }

    public sealed class ScenarioTheHostStopsMidRender
    {
        [Fact]
        public async Task TheCancellationEscapesTheTick()
        {
            var rig = SweepRig.AfterSwitch(staleItems: 1, gated: true);
            using var stopping = new CancellationTokenSource();

            var tick = rig.Resweeper.TickOnceAsync(stopping.Token);
            await rig.Author.Started.WaitAsync(TimeSpan.FromSeconds(10));
            await stopping.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tick.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    public sealed class ScenarioTheReplacementWasSwitchedOffBeforeTheOldRowWas : IAsyncLifetime
    {
        readonly SweepRig rig = SweepRig.WithASwapWhoseReplacementIsOff();

        public async Task InitializeAsync()
        {
            // A render would only muddy what the swap left behind, so it fails.
            rig.Author.NextResult = SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, "engine down");
            await rig.Resweeper.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOldRowKeepsAiring() => Assert.True(rig.Catalog.IsEligible(SweepRig.FirstOldMediaId));

        [Fact]
        public void TheLedgerGoesBackToTheOldRow() =>
            Assert.Equal(SweepRig.FirstOldMediaId, Assert.Single(rig.Ledger.Records).MediaId);
    }
}
