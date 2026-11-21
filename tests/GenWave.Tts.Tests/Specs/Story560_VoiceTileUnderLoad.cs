// STORY-560 — Engine down means music (SPEC F219.25 · PLAN T768a)
//
// BDD specification — xUnit. The voice tile stays honest under load: a speech render in flight to the active engine,
// or one that succeeded within the last check interval, counts as reachable in place of the OPTIONS ping. Two failed or
// timed-out renders in a row (no success between) turn it red; one bad line alone is a grace miss (Dean's ruling).

using System.Net;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureVoiceTileUnderLoad
{
    static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    static readonly TimeSpan PastTheLimit = TimeSpan.FromSeconds(31);   // the engine's limit is 30 s

    static bool IsGreen(VoiceTileHarness tile) => tile.Store.GetVerdict(tile.Key)?.Healthy == true;

    // Happy path: warmed engine, then a long render to it that stops it answering pings.
    public sealed class ScenarioARenderIsInFlight : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();
        Task<string>? render;

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();                                         // boot warm-up: green
            var began = tile.Engines.HoldNextRender(VoiceTileHarness.Host);
            render = tile.StartRenderAsync(tile.Engine);
            await began.WaitAsync(Guard);
            await tile.CycleAsync();                                         // the ping would time out here
            await tile.CycleAsync();
        }

        public async Task DisposeAsync()
        {
            tile.Engines.Finish(VoiceTileHarness.Host, HttpStatusCode.OK);
            if (render is not null)
                await render.WaitAsync(Guard);
            tile.Dispose();
        }

        [Fact]
        public void TheTileStaysGreen() => Assert.True(IsGreen(tile));

        [Fact]
        public void NoSecondWarmUpLineIsRendered() =>
            Assert.Equal(2, tile.Engines.Renders(VoiceTileHarness.Host));   // the boot line and the on-air render

        [Fact]
        public void NoPingIsSent() =>
            Assert.Equal(0, tile.Engines.Pings(VoiceTileHarness.Host));
    }

    public sealed class ScenarioARenderJustSucceeded : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.StartRenderAsync(tile.Engine).WaitAsync(Guard);
            tile.Time.Advance(TimeSpan.FromSeconds(5));                      // inside the 30 s interval
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void NoPingIsSent() =>
            Assert.Equal(0, tile.Engines.Pings(VoiceTileHarness.Host));

        [Fact]
        public void TheTileIsGreen() => Assert.True(IsGreen(tile));
    }

    // The ping still does its job when there is no render evidence.
    public sealed class ScenarioNoRecentRender : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();                                         // warm-up render is the last success
            tile.Time.Advance(TimeSpan.FromSeconds(31));                     // past the 30 s interval
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheEngineIsPinged() =>
            Assert.Equal(1, tile.Engines.Pings(VoiceTileHarness.Host));
    }

    // Grace: one bad line is not an outage.
    public sealed class ScenarioOneRenderFails : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderAnsweredWithAsync(HttpStatusCode.InternalServerError);
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileStaysGreen() => Assert.True(IsGreen(tile));
    }

    public sealed class ScenarioFailSucceedFail : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderAnsweredWithAsync(HttpStatusCode.InternalServerError);
            await tile.RenderAnsweredWithAsync(HttpStatusCode.OK);
            await tile.RenderAnsweredWithAsync(HttpStatusCode.InternalServerError);
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileStaysGreen() => Assert.True(IsGreen(tile));
    }

    // Sad path: two in a row.
    public sealed class ScenarioTwoRendersFailInARow : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderAnsweredWithAsync(HttpStatusCode.InternalServerError);
            await tile.RenderAnsweredWithAsync(HttpStatusCode.BadGateway);
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileIsRed() => Assert.False(IsGreen(tile));

        [Fact]
        public void TheTileGivesTheReason() =>
            Assert.Contains("502", tile.Store.GetVerdict(tile.Key)?.Reason);
    }

    public sealed class ScenarioTwoRendersRunPastTheTimeLimit : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderCancelledByCallerAfterAsync(PastTheLimit);      // the caller's budget fires
            await tile.RenderCancelledByCallerAfterAsync(PastTheLimit);
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileIsRed() => Assert.False(IsGreen(tile));

        [Fact]
        public void TheTileGivesTheReason() =>
            Assert.Equal("Timed out after 30 s", tile.Store.GetVerdict(tile.Key)?.Reason);
    }

    // The feeder never cancels at the budget: it gives up on the line and lets the render finish.
    public sealed class ScenarioTwoRendersFinishAfterTheTimeLimit : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderFinishingAfterAsync(TimeSpan.FromSeconds(45));
            await tile.RenderFinishingAfterAsync(TimeSpan.FromSeconds(45));
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileIsRed() => Assert.False(IsGreen(tile));

        [Fact]
        public void TheTileGivesTheReason() =>
            Assert.Equal("Took 45 s, over the 30 s limit", tile.Store.GetVerdict(tile.Key)?.Reason);
    }

    public sealed class ScenarioTwoRendersHitTheHttpTimeout : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderThatHttpTimesOutAsync();
            await tile.RenderThatHttpTimesOutAsync();
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileIsRed() => Assert.False(IsGreen(tile));

        [Fact]
        public void TheReasonNamesTheHttpClientTimeout() =>
            Assert.Equal("Timed out after 100 s", tile.Store.GetVerdict(tile.Key)?.Reason);
    }

    // Not the engine's fault.
    public sealed class ScenarioTheCallerCancelsTheRender : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderCancelledByCallerAfterAsync(TimeSpan.FromSeconds(2));
            await tile.RenderCancelledByCallerAfterAsync(TimeSpan.FromSeconds(2));
            tile.Time.Advance(TimeSpan.FromSeconds(60));                     // no render evidence left: the ping decides
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileIsNotRed() => Assert.True(IsGreen(tile));
    }

    public sealed class ScenarioOnAirRendersAreRefusedAsBadRequests : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderAnsweredWithAsync(HttpStatusCode.BadRequest);
            await tile.RenderAnsweredWithAsync(HttpStatusCode.UnprocessableEntity);
            tile.Time.Advance(TimeSpan.FromSeconds(60));
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileIsNotRed() => Assert.True(IsGreen(tile));

        [Fact]
        public void NoSuccessCreditIsGiven() =>
            Assert.Equal(1, tile.Engines.Pings(VoiceTileHarness.Host));      // the ping still had to decide
    }

    public sealed class ScenarioFailedAuditions : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            await tile.RenderAnsweredWithAsync(HttpStatusCode.InternalServerError, audition: true);
            await tile.RenderAnsweredWithAsync(HttpStatusCode.InternalServerError, audition: true);
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void TheTileIsNotRed() => Assert.True(IsGreen(tile));
    }

    public sealed class ScenarioASuccessfulAudition : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            tile.Time.Advance(TimeSpan.FromSeconds(60));
            await tile.RenderAnsweredWithAsync(HttpStatusCode.OK, audition: true);
            await tile.CycleAsync();
        }

        public Task DisposeAsync()
        {
            tile.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void GivesNoSuccessCredit() =>
            Assert.Equal(1, tile.Engines.Pings(VoiceTileHarness.Host));
    }

    // A switch to another engine ignores what the old one's renders said.
    public sealed class ScenarioTheEngineChanges : IAsyncLifetime
    {
        readonly VoiceTileHarness tile = new();
        Task<string>? render;

        public async Task InitializeAsync()
        {
            await tile.CycleAsync();
            var began = tile.Engines.HoldNextRender(VoiceTileHarness.Host);
            render = tile.StartRenderAsync(tile.Engine);                     // in flight on the OLD engine
            await began.WaitAsync(Guard);
            tile.Active.Current = tile.OtherEngine;
            await tile.CycleAsync();                                         // new engine: warm-up line, not a skipped check
            tile.Time.Advance(TimeSpan.FromSeconds(31));                     // its warm-up line is stale; the old engine's render is not its evidence
            await tile.CycleAsync();                                         // so this one pings
        }

        public async Task DisposeAsync()
        {
            tile.Engines.Finish(VoiceTileHarness.Host, HttpStatusCode.OK);
            if (render is not null)
                await render.WaitAsync(Guard);
            tile.Dispose();
        }

        [Fact]
        public void TheNewEngineGetsItsOwnWarmUpLine() =>
            Assert.Equal(1, tile.Engines.Renders(VoiceTileHarness.OtherHost));

        [Fact]
        public void TheNewEngineIsPingedNotAssumedReachable() =>
            Assert.Equal(1, tile.Engines.Pings(VoiceTileHarness.OtherHost));
    }

    // The tracker's own bookkeeping.
    public sealed class ScenarioTheTrackerCountsFailures
    {
        const string Engine = "chatterbox|http://chatterbox:8004";

        [Fact]
        public void ASuccessResetsTheCountInARow()
        {
            var activity = new EngineRenderActivity();
            activity.Failed(Engine, "boom");
            activity.Succeeded(Engine);

            Assert.Equal(0, activity.Read(Engine).FailuresInARow);
        }

        [Fact]
        public void AFailureIsConsumedOnce()
        {
            var activity = new EngineRenderActivity();
            activity.Failed(Engine, "boom");
            activity.Failed(Engine, "boom");
            Assert.True(activity.Read(Engine).IsDown);

            activity.ClearFailure(Engine);

            Assert.False(activity.Read(Engine).IsDown);
        }
    }
}
