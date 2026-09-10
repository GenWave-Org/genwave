// STORY-422 — ExecuteAsync's own consumer loop survives a single job's own failure, whatever shape
// that failure takes, because RunOneAsync's own catch-all is what actually resolves it (PLAN T441
// ruling). A CancelAsync racing a job still at its own start line, and an exception raised BY that
// start line's own log call, both land inside RunOneAsync's own handler once its try block starts at
// that line — proven here by the same discriminator: zero warnings from ExecuteAsync's own outer catch,
// which would log one for anything that instead escaped that far.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using GenWave.Tts;
using Microsoft.Extensions.Configuration;

public static class FeatureTheConsumerLoopSurvivesAJobThatFailsInsideItsOwnHandler
{
    public sealed class ScenarioACancelLandsWhileAJobIsStarting
    {
        [Fact]
        public async Task TheRacingCancelResolvesInsideTheJobsOwnHandlerAndTheQueueLivesOn()
        {
            const long spotId = 1;
            const long secondSpotId = 2;
            const long sponsorId = 1;

            var store = new FakeAdSpotJobStore();
            store.Seed(spotId, sponsorId, "A deal so good it's almost illegal.");
            store.Seed(secondSpotId, sponsorId, "Come on down.");
            var sponsors = new FakeSponsorStore(_ => "Cravin's Diner");
            var handler = AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply);
            var llmOptions = new FakeOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 300,
            });
            var recorder = new LlmCallRecorder(new LlmCallRing(llmOptions), new LlmCallCauseCounters(TimeProvider.System));
            var scriptWriter = new AdScriptWriter(
                new SingleHandlerHttpClientFactory(handler), llmOptions, recorder, new FakeDegradationModeReader(),
                new NoOpLogger<AdScriptWriter>(), TimeProvider.System);
            var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions());
            var gate = new FakeOnAirRenderSignal { InFlight = false };
            var logger = new GatedStartLogger(spotId);

            // renderService/stamper/configuration only reach RunPreviewAsync (PLAN T442) — this
            // scenario only ever enqueues "write" jobs, so these stay wired but unreached.
            var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", "Test Station", "station_voice"));
            var libraries = new FakeAdsLibraryStore();
            var locatorRoots = new AdSpotLocatorRoots("/media", "/authored");
            var renderService = new AdRenderService(
                new FakeCastSegmentAuthor(), store, new FakeAdminMediaLookup(), libraries, stationIdentity, adsOptions,
                locatorRoots, new NoOpLogger<AdRenderService>());
            var stamper = new AdSpotStamper(
                store, new FakeAdBedPool(), libraries, stationIdentity, adsOptions, new NoOpLogger<AdSpotStamper>());
            var configuration = new ConfigurationBuilder().Build();

            var service = new AdSpotJobService(
                store, sponsors, scriptWriter, renderService, stamper, new FakePatterDurationEstimator(),
                new FakeAudiencePostureProvider(), gate, adsOptions, llmOptions, configuration, TimeProvider.System,
                logger);

            await service.StartAsync(CancellationToken.None);
            try
            {
                var enqueued = await service.TryEnqueueAsync(spotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, enqueued);

                // Waits for RunOneAsync's own "starting" line to be logged AND parked there — the exact
                // window between its token lookup and its own use of that token this fact races
                // CancelAsync against. A bounded wait on the observed flag rather than an unbounded
                // await on the gate's own Task, so a wording drift that stops GatedStartLogger matching
                // AdSpotJobService.JobStartingLogPrefix fails this fact outright instead of hanging it.
                var started = await JobWait.TryWaitUntilAsync(() => logger.HasStarted, TimeSpan.FromSeconds(5));
                Assert.True(started, "the first job's own start line was never logged");

                await service.CancelAsync(spotId, CancellationToken.None);
                logger.Release();

                var secondEnqueued = await service.TryEnqueueAsync(secondSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, secondEnqueued);

                var ran = await JobWait.TryWaitUntilAsync(
                    () => store.ClearJobCallCount(secondSpotId) >= 1, TimeSpan.FromSeconds(5));

                // The claim itself: the racing cancel on the FIRST job must never take the consumer
                // loop down with it, so the SECOND job — enqueued only after the race — still runs to
                // its own completion.
                Assert.True(ran, "the second job never reached its own ClearJobAsync call");

                // The discriminator a bare "did the second job run" assertion cannot provide on its own
                // (PLAN T441 ruling): ExecuteAsync's own outer catch would also rescue a job that
                // escapes RunOneAsync entirely, letting the second job run regardless — but it always
                // logs a warning for the job it rescued. Zero warnings means the race resolved through
                // RunOneAsync's OWN cancellation handling, exactly as this fact requires.
                Assert.Equal(0, logger.WarningCount);

                Assert.False(service.ExecuteTask?.IsFaulted ?? false);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                service.Dispose();
            }
        }
    }

    public sealed class ScenarioTheStartLineThrowsForOneJob
    {
        [Fact]
        public async Task TheFailedJobIsClearedWithAnErrorAndTheQueueLivesOn()
        {
            const long firstSpotId = 1;
            const long secondSpotId = 2;
            const long sponsorId = 1;

            var store = new FakeAdSpotJobStore();
            store.Seed(firstSpotId, sponsorId, "A deal so good it's almost illegal.");
            store.Seed(secondSpotId, sponsorId, "Come on down.");
            var sponsors = new FakeSponsorStore(_ => "Cravin's Diner");
            var handler = AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply);
            var llmOptions = new FakeOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 300,
            });
            var recorder = new LlmCallRecorder(new LlmCallRing(llmOptions), new LlmCallCauseCounters(TimeProvider.System));
            var scriptWriter = new AdScriptWriter(
                new SingleHandlerHttpClientFactory(handler), llmOptions, recorder, new FakeDegradationModeReader(),
                new NoOpLogger<AdScriptWriter>(), TimeProvider.System);
            var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions());
            var gate = new FakeOnAirRenderSignal { InFlight = false };
            var logger = new ThrowOnceStartLogger();

            // renderService/stamper/configuration only reach RunPreviewAsync (PLAN T442) — this
            // scenario only ever enqueues "write" jobs, so these stay wired but unreached.
            var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", "Test Station", "station_voice"));
            var libraries = new FakeAdsLibraryStore();
            var locatorRoots = new AdSpotLocatorRoots("/media", "/authored");
            var renderService = new AdRenderService(
                new FakeCastSegmentAuthor(), store, new FakeAdminMediaLookup(), libraries, stationIdentity, adsOptions,
                locatorRoots, new NoOpLogger<AdRenderService>());
            var stamper = new AdSpotStamper(
                store, new FakeAdBedPool(), libraries, stationIdentity, adsOptions, new NoOpLogger<AdSpotStamper>());
            var configuration = new ConfigurationBuilder().Build();

            var service = new AdSpotJobService(
                store, sponsors, scriptWriter, renderService, stamper, new FakePatterDurationEstimator(),
                new FakeAudiencePostureProvider(), gate, adsOptions, llmOptions, configuration, TimeProvider.System,
                logger);

            await service.StartAsync(CancellationToken.None);
            try
            {
                var firstEnqueued = await service.TryEnqueueAsync(firstSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, firstEnqueued);

                var secondEnqueued = await service.TryEnqueueAsync(secondSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, secondEnqueued);

                // The first job's own start line is what actually threw — not merely inferred from the
                // row ending up cleared, which a completely different failure could also produce.
                var firstCleared = await JobWait.TryWaitUntilAsync(
                    () => store.ClearJobCallCount(firstSpotId) == 1, TimeSpan.FromSeconds(5));
                Assert.True(firstCleared, "the first job never reached its own single ClearJobAsync call");
                Assert.True(logger.Thrown, "the start line never threw — the fake no longer matches the production log message");

                var firstSpot = await store.GetByIdAsync(firstSpotId, CancellationToken.None);
                Assert.NotNull(firstSpot?.JobError);

                // The claim this fact exists to make: RunOneAsync's own catch-all is what cleared the
                // first job's row — the queue's single consumer thread is still alive to dequeue and run
                // a second job afterward, rather than the whole loop having gone down with the first.
                var secondRan = await JobWait.TryWaitUntilAsync(
                    () => store.ClearJobCallCount(secondSpotId) >= 1, TimeSpan.FromSeconds(5));
                Assert.True(secondRan, "the second job never reached its own ClearJobAsync call");

                Assert.False(service.ExecuteTask?.IsFaulted ?? false);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                service.Dispose();
            }
        }
    }
}
